using DigitalBrain.Microsoft.CSharp;
using System.Text;
using System.Text.Json;
using DigitalBrain.AI;
using DigitalBrain.AI.OpenAI;
using DigitalBrain.Apps;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace IntoChat.Marketplace;

// An app someone is creating by describing it. The Author agent writes its spec in plain language;
// the person reads and edits it; the Builder agent then writes the tests and the implementation, and
// the app is published only once its tests run green. Keyed "{owner}/drafts/{id}".
[Alias("intochat.app-draft"), Orleans.Metadata.DefaultGrainType("intochat.app-draft")]
public interface IAppDraft : INeuron
{
    [ResponseTimeout("00:10:00")] Task<AppDraftView> Draft(string request);
    [ResponseTimeout("00:10:00")] Task<AppDraftView> Revise(string instruction);
    Task<AppDraftView> EditSpec(string spec);
    [ResponseTimeout("02:00:00")] Task<AppDraftView> Build();
    [ReadOnly, AlwaysInterleave] Task<AppDraftView> Read();
}

public enum AppDraftStatus { Empty, Drafted, Building, Published, Failed }

[GenerateSerializer, Alias("intochat.app-draft-attempt")]
public sealed record AppDraftAttempt(
    [property: Id(0)] string Revision,
    [property: Id(1)] bool Green,
    [property: Id(2)] string Failures);

[GenerateSerializer, Alias("intochat.app-draft-state")]
public sealed record AppDraftState
{
    [Id(0)] public long Revision { get; init; }
    [Id(1)] public string Request { get; init; } = "";
    [Id(2)] public string Name { get; init; } = "";
    [Id(3)] public string Title { get; init; } = "";
    [Id(4)] public string Description { get; init; } = "";
    [Id(5)] public string Runtime { get; init; } = "";
    [Id(6)] public string Spec { get; init; } = "";
    [Id(7)] public AppDraftStatus Status { get; init; }
    [Id(8)] public IReadOnlyList<AppDraftAttempt> Attempts { get; init; } = [];
    [Id(9)] public PackageRevisionRef? Published { get; init; }
    [Id(10)] public string Error { get; init; } = "";
}

[GenerateSerializer, Alias("intochat.app-draft-view")]
public sealed record AppDraftView(
    [property: Id(0)] AppDraftState Draft,
    [property: Id(1)] AppVerification? Verification);

[GenerateSerializer, Alias("intochat.app-draft-changed")]
public sealed record AppDraftChanged([property: Id(0)] string DraftId, [property: Id(1)] long Revision, [property: Id(2)] AppDraftStatus Status) : Signal;

[GrainType("intochat.app-draft")]
internal sealed class AppDraftNeuron(
    [PersistentState("intochat.app-draft", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<AppDraftState> store,
    IConfiguration configuration,
    CSharpToolService? csharp = null)
    : Neuron<AppDraftState>(store), IAppDraft
{
    private const int MaxAuthorRetries = 2;
    private const int MaxBuildAttempts = 3;
    private const int MaxRequestLength = 4000;
    private static readonly string[] ConfigurationRuntimes = [GroupChatRuntime.RuntimeName, "prompt"];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private string DraftId => this.GetPrimaryKeyString();
    private string Owner => DraftId.Split('/')[0];
    private string AuthorModel => configuration["IntoChat:Apps:AuthorModel"] ?? nameof(IGpt56Luna);
    private string BuilderModel => configuration["IntoChat:Apps:BuilderModel"] ?? nameof(IGpt56Luna);
    // A csharp app is only offered where this host may run it.
    private string[] Runtimes => csharp?.AllowActivation == true ? [PackageManifest.CSharpRuntime, .. ConfigurationRuntimes] : ConfigurationRuntimes;

    public async Task<AppDraftView> Draft(string request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request);
        if (request.Length > MaxRequestLength) { throw new ArgumentException($"Describe the app in at most {MaxRequestLength} characters."); }
        return await Author(Snapshot with { Request = request.Trim() },
            $"Write the specification for this app.\n\nWhat the person wants:\n{request.Trim()}");
    }

    public Task<AppDraftView> Revise(string instruction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instruction);
        RequireSpec();
        return Author(Snapshot,
            $"Revise this specification.\n\nWhat the person originally wanted:\n{Snapshot.Request}\n\nCurrent name: {Snapshot.Name}\nCurrent title: {Snapshot.Title}\nCurrent description: {Snapshot.Description}\nCurrent runtime: {Snapshot.Runtime}\n\nCurrent specification:\n{Snapshot.Spec}\n\nTheir change request:\n{instruction.Trim()}\n\nReply with the current name, title, description and runtime unless the change requires otherwise.");
    }

    public async Task<AppDraftView> EditSpec(string spec)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spec);
        RequireSpec();
        await Persist(Snapshot with { Spec = spec, Status = AppDraftStatus.Drafted, Error = "" });
        return await Read();
    }

    public async Task<AppDraftView> Build()
    {
        RequireSpec();
        if (!Runtimes.Contains(Snapshot.Runtime)) { throw new InvalidOperationException(MarketplaceService.ActivationDisabled); }
        await Persist(Snapshot with { Status = AppDraftStatus.Building, Attempts = [], Error = "" });
        var package = GrainFactory.GetGrain<IPackage>(PackageId.Create(Owner, Snapshot.Name).ToString());
        var failures = "";
        for (var attempt = 1; attempt <= MaxBuildAttempts; attempt++)
        {
            try
            {
                var content = await Implement(failures);
                var head = (await package.Read()).Head;
                var revision = await package.Commit(new CommitPackage(Guid.NewGuid(), head, content, $"Build attempt {attempt}: {Snapshot.Title}"));
                var reference = new PackageRevisionRef(PackageId.Create(Owner, Snapshot.Name), revision.Id);
                var verification = await GrainFactory.GetGrain<IAppVerification>(IAppVerification.Key(reference)).Verify();
                failures = Failures(verification.Run);
                await Persist(Snapshot with { Attempts = [.. Snapshot.Attempts, new(revision.Id, verification.Green, failures)] });
                if (verification.Green)
                {
                    await package.Publish(new PublishPackage(Guid.NewGuid(), revision.Id));
                    await Persist(Snapshot with { Status = AppDraftStatus.Published, Published = reference });
                    return await Read();
                }
            }
            catch (Exception error) when (error is ArgumentException or JsonException or InvalidDataException)
            {
                failures = $"The implementation was rejected: {error.Message}";
                await Persist(Snapshot with { Attempts = [.. Snapshot.Attempts, new("", false, failures)] });
            }
        }
        await Persist(Snapshot with { Status = AppDraftStatus.Failed, Error = $"The tests still fail after {MaxBuildAttempts} attempts." });
        return await Read();
    }

    public async Task<AppDraftView> Read()
    {
        var last = Snapshot.Name.Length == 0 ? null : Snapshot.Attempts.LastOrDefault(attempt => attempt.Revision.Length > 0);
        var verification = last is null
            ? null
            : await GrainFactory.GetGrain<IAppVerification>(IAppVerification.Key(new(PackageId.Create(Owner, Snapshot.Name), last.Revision))).Read();
        return new(Snapshot, verification);
    }

    private async Task<AppDraftView> Author(AppDraftState draft, string task)
    {
        var prompt = $"{task}\n\nRuntimes this brain can run: {string.Join(", ", Runtimes)}.";
        for (var retry = 0; ; retry++)
        {
            AuthoredApp authored;
            PackageId package;
            try
            {
                authored = Parse<AuthoredApp>(await ModelAddress.Complete(GrainFactory, AuthorModel, AgentPrompts.Author, prompt));
                if (!Runtimes.Contains(authored.Runtime)) { throw new InvalidDataException($"'{authored.Runtime}' is not one of the runtimes {string.Join(", ", Runtimes)}."); }
                package = PackageId.Create(Owner, authored.Name);
            }
            catch (Exception error) when (error is JsonException or InvalidDataException or ArgumentException && retry < MaxAuthorRetries)
            {
                prompt += $"\n\nYour previous reply could not be used: {error.Message} Reply with the JSON object only.";
                continue;
            }
            await Persist(draft with
            {
                Name = package.Name, Title = authored.Title, Description = authored.Description, Runtime = authored.Runtime,
                Spec = authored.Spec, Status = AppDraftStatus.Drafted, Attempts = [], Error = "",
            });
            return await Read();
        }
    }

    private async Task<PackageContent> Implement(string failures)
    {
        var task = new StringBuilder()
            .AppendLine($"Runtime: {Snapshot.Runtime}")
            .AppendLine($"Package: {Owner}/{Snapshot.Name}")
            .AppendLine($"What the person wants: {Snapshot.Request}")
            .AppendLine().AppendLine("Specification:").AppendLine(Snapshot.Spec);
        if (failures.Length > 0) { task.AppendLine().AppendLine("The previous attempt failed:").AppendLine(failures); }
        var built = Parse<BuiltApp>(await ModelAddress.Complete(GrainFactory, BuilderModel, AgentPrompts.Builder, task.ToString()));
        var files = new Dictionary<string, string>(built.Files ?? new Dictionary<string, string>(), StringComparer.Ordinal)
        {
            [PackageContent.SpecPath] = Snapshot.Spec,
        };
        if (!files.ContainsKey(PackageContent.TestsPath))
        { throw new InvalidDataException($"The implementation must include {PackageContent.TestsPath}, the scenarios' proof."); }
        return new PackageContent(
            new PackageManifest(Snapshot.Title, Snapshot.Description, [new PackageOperation("ask", "Ask the app.")],
                [.. (built.Settings ?? []).Select(setting => new PackageSetting(setting.Name, setting.Description ?? "", setting.Default ?? ""))],
                Runtime: Snapshot.Runtime),
            built.Source ?? "",
            files);
    }

    private static string Failures(AppTestRun run)
    {
        if (run.Scenarios.Length == 0) { return $"The tests reported no scenarios (exit code {run.ExitCode?.ToString() ?? "unknown"})."; }
        return string.Join("\n", run.Scenarios
            .Where(scenario => !scenario.Passed)
            .Select(scenario => $"Scenario '{scenario.Name}': {scenario.Message}"));
    }

    // Models sometimes wrap the JSON in a code fence or add prose around it; read the first complete object.
    private static T Parse<T>(string reply)
    {
        var start = reply.IndexOf('{', StringComparison.Ordinal);
        if (start < 0) { throw new InvalidDataException("The reply contains no JSON object."); }
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(reply[start..]));
        using var document = JsonDocument.ParseValue(ref reader);
        return document.Deserialize<T>(Json) ?? throw new InvalidDataException("The reply's JSON object is empty.");
    }

    private void RequireSpec()
    {
        if (Snapshot.Spec.Length == 0) { throw new InvalidOperationException("Describe the app first."); }
        if (Snapshot.Status == AppDraftStatus.Building) { throw new InvalidOperationException("The app is being built."); }
    }

    private Task Persist(AppDraftState next)
    {
        var saved = next with { Revision = Snapshot.Revision + 1 };
        return Save(saved, new AppDraftChanged(DraftId, saved.Revision, saved.Status));
    }

    private sealed record AuthoredApp(string Name, string Title, string Description, string Runtime, string Spec);
    private sealed record BuiltApp(IReadOnlyList<BuiltSetting>? Settings, IReadOnlyDictionary<string, string>? Files, string? Source);
    private sealed record BuiltSetting(string Name, string? Description, string? Default);
}

internal static class AgentPrompts
{
    public static string Author { get; } = Load("author.md");
    public static string Builder { get; } = Load("builder.md");

    private static string Load(string name)
    {
        using var stream = typeof(AgentPrompts).Assembly.GetManifestResourceStream("IntoChat.Agents." + name)
            ?? throw new InvalidOperationException($"The {name} agent prompt is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
