using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using DigitalBrain.AI;
using DigitalBrain.AI.OpenAI;
using DigitalBrain.Apps;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Apps;

[GrainType("intochat.app-draft")]
internal sealed class AppDraftNeuron(
    [PersistentState("intochat.app-draft", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<AppDraftState> store,
    IConfiguration configuration,
    ILogger<AppDraftNeuron> logger,
    IScriptSandbox? csharp = null)
    : Neuron<AppDraftState>(store), IAppDraft
{
    private const int MaxAuthorRetries = 2;
    private const int MaxBuildAttempts = 3;
    private const int MaxBuilderRounds = 12;
    private const int MaxRequestLength = 4000;
    private static readonly string[] ConfigurationRuntimes = [GroupChatRuntime.RuntimeName, "prompt"];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private string DraftId => this.GetPrimaryKeyString();
    private string Owner => DraftId.Split('/')[0];
    private string AuthorModel => configuration["IntoChat:Apps:AuthorModel"] ?? nameof(IGpt56Luna);
    private string BuilderModel => configuration["IntoChat:Apps:BuilderModel"] ?? nameof(IGpt56Luna);
    // A csharp app is only offered where this host may run it.
    private string[] Runtimes => csharp?.CanRun == true ? [PackageManifest.CSharpRuntime, .. ConfigurationRuntimes] : ConfigurationRuntimes;

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
        // Verification runs tests.cs as a sandbox script whatever the app's own runtime is, so a
        // sandbox-less host refuses here instead of burning build attempts that can only fail.
        if (csharp?.CanRun != true || !Runtimes.Contains(Snapshot.Runtime)) { throw new InvalidOperationException(MarketplaceService.SandboxMissing); }
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
            // Any failure is one attempt, never an escape: a build must end Published or Failed, or
            // the draft would stay Building forever (for example when this host cannot run tests).
            catch (Exception error) when (error is not OperationCanceledException)
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
                Name = package.Name,
                Title = authored.Title,
                Description = authored.Description,
                Runtime = authored.Runtime,
                Spec = authored.Spec,
                Status = AppDraftStatus.Drafted,
                Attempts = [],
                Error = "",
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
        var built = Parse<BuiltApp>(await BuilderConversation(task.ToString()));
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

    // The Builder works with tools: it searches the registry for contracts, reads their details and
    // compile-checks its C# before answering. A model that never calls a tool (a scripted one, or a
    // confident real one) simply answers in the first round.
    private async Task<string> BuilderConversation(string task)
    {
        var model = ModelAddress.Resolve(GrainFactory, BuilderModel);
        var messages = new List<AiMessage>
        {
            new("system", [new AiText(AgentPrompts.Builder)]),
            new("user", [new AiText(task)]),
        };
        for (var round = 0; round < MaxBuilderRounds; round++)
        {
            var result = await model.Generate(new InferenceRequest(messages, Tools: BuilderTools.Definitions));
            messages.AddRange(result.Messages);
            var calls = result.Messages.SelectMany(message => message.Content).OfType<AiToolCall>().ToArray();
            if (calls.Length == 0)
            {
                return string.Concat(result.Messages.SelectMany(message => message.Content).OfType<AiText>().Select(content => content.Text)).Trim();
            }
            var results = new List<AiContent>();
            foreach (var call in calls) { results.Add(new AiToolResult(call.CallId, await ExecuteBuilderTool(call))); }
            messages.Add(new AiMessage("tool", results));
        }
        throw new InvalidDataException($"The Builder used tools for {MaxBuilderRounds} rounds without producing the implementation.");
    }

    private async Task<string> ExecuteBuilderTool(AiToolCall call)
    {
        try
        {
            return call.Name switch
            {
                BuilderTools.SearchContracts => JsonSerializer.Serialize(
                    await BuilderTools.Search(GrainFactory, Parse<BuilderTools.SearchArguments>(call.ArgumentsJson).Query), Json),
                BuilderTools.ReadContracts => JsonSerializer.Serialize(
                    await Require(csharp).ReadContracts(Parse<BuilderTools.ReadArguments>(call.ArgumentsJson).Modules ?? [], CancellationToken.None), Json),
                BuilderTools.CheckCSharp => JsonSerializer.Serialize(
                    Require(csharp).Check(Parse<BuilderTools.CheckArguments>(call.ArgumentsJson).Files ?? new Dictionary<string, string>()), Json),
                _ => throw new ArgumentException($"Unknown tool '{call.Name}'."),
            };
        }
        // A failed tool call is the model's problem to correct, not the build's end.
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return JsonSerializer.Serialize(new { error = error.Message }, Json);
        }

        static T Require<T>(T? service) where T : class
            => service ?? throw new InvalidOperationException(MarketplaceService.SandboxMissing);
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

    private async Task Persist(AppDraftState next)
    {
        var saved = next with { Revision = Snapshot.Revision + 1 };
        await Save(saved, new AppDraftChanged(DraftId, saved.Revision, saved.Status));
        // The index is a read model, so a missed write only stales the list until the next one;
        // failing here instead could strand a Build or bury a published result.
        try { await GrainFactory.GetGrain<IAppDrafts>(Owner).Record(DraftId.Split('/')[2], saved.Title, saved.Status); }
        catch (Exception error) when (error is not OperationCanceledException)
        { logger.LogWarning(error, "The drafts index for {Owner} missed {DraftId}.", Owner, DraftId); }
    }

    private sealed record AuthoredApp(string Name, string Title, string Description, string Runtime, string Spec);
    private sealed record BuiltApp(IReadOnlyList<BuiltSetting>? Settings, IReadOnlyDictionary<string, string>? Files, string? Source);
    private sealed record BuiltSetting(string Name, string? Description, string? Default);
}


