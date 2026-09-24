using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Apps.Signals;
using DigitalBrain.Behavior;
using DigitalBrain.Coding;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Apps;

[GrainType("apps.app")]
internal sealed class App(
    [PersistentState("app", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<AppState> store,
    TimeProvider clock)
    : Neuron<AppState>(store), IApp
{
    private const int MaxInvocations = 256;
    private const int MaxSettingLength = 4096;
    private const int MaxPayloadLength = 64 * 1024;

    public Task<AppSnapshot> Read() => Task.FromResult(Describe(Snapshot));

    public async Task<AppSnapshot> Install(InstallApp request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Replay(request.OperationId, request)) { return Describe(Snapshot); }
        if (Snapshot.Status == AppStatus.Installed)
        { throw new InvalidOperationException($"{Snapshot.Revision!.Package} is already installed here; configure or upgrade it instead."); }
        var revision = await GrainFactory.GetGrain<IPackage>(request.Revision.Package.ToString()).ReadRevision(request.Revision.Revision);
        var settings = Resolve(revision.Content.Manifest.Settings, new Dictionary<string, string>(), request.Settings);
        var installation = Snapshot.Installation + 1;
        await Deploy(ProgramKey(installation), request.OperationId, revision.Artifact, settings);
        await Persist(Snapshot with
        {
            Status = AppStatus.Installed,
            Revision = request.Revision,
            Artifact = revision.Artifact,
            Declared = [.. revision.Content.Manifest.Settings],
            Settings = settings,
            Operations = [.. revision.Content.Manifest.Operations],
            Installation = installation,
            Receipts = Receipted(request.OperationId, request),
        });
        return Describe(Snapshot);
    }

    public async Task<AppSnapshot> Configure(ConfigureApp request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Replay(request.OperationId, request)) { return Describe(Snapshot); }
        RequireInstalled();
        var settings = Resolve(Snapshot.Declared, Snapshot.Settings, request.Settings);
        await Deploy(ProgramKey(Snapshot.Installation), request.OperationId, Snapshot.Artifact!, settings);
        await Persist(Snapshot with { Settings = settings, Receipts = Receipted(request.OperationId, request) });
        return Describe(Snapshot);
    }

    public async Task<AppSnapshot> Upgrade(UpgradeApp request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Replay(request.OperationId, request)) { return Describe(Snapshot); }
        RequireInstalled();
        var installed = Snapshot.Revision!.Package;
        if (request.Revision.Package != installed)
        { throw new ArgumentException($"This app runs {installed}. Install {request.Revision.Package} as its own app to try it."); }
        var revision = await GrainFactory.GetGrain<IPackage>(installed.ToString()).ReadRevision(request.Revision.Revision);
        // Keep what the installer chose for settings the new revision still declares.
        var declared = revision.Content.Manifest.Settings;
        var kept = Snapshot.Settings.Where(pair => declared.Any(setting => setting.Name == pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);
        var settings = Resolve(declared, kept, new Dictionary<string, string>());
        await Deploy(ProgramKey(Snapshot.Installation), request.OperationId, revision.Artifact, settings);
        await Persist(Snapshot with
        {
            Revision = request.Revision,
            Artifact = revision.Artifact,
            Declared = [.. declared],
            Settings = settings,
            Operations = [.. revision.Content.Manifest.Operations],
            Receipts = Receipted(request.OperationId, request),
        });
        return Describe(Snapshot);
    }

    public async Task<AppSnapshot> Uninstall(UninstallApp request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Replay(request.OperationId, request)) { return Describe(Snapshot); }
        RequireInstalled();
        var program = GrainFactory.GetGrain<IBehaviorProgram>(ProgramKey(Snapshot.Installation));
        await program.Delete(new DeleteBehavior((await program.Read()).Revision, request.OperationId));
        var now = clock.GetUtcNow();
        var invocations = Snapshot.Invocations
            .Select(item => item.Status == InvocationStatus.Pending ? item with { Status = InvocationStatus.Failed, Error = "The app was uninstalled.", CompletedAt = now } : item)
            .ToList();
        await Persist(Snapshot with { Status = AppStatus.Uninstalled, Invocations = invocations, Receipts = Receipted(request.OperationId, request) });
        return Describe(Snapshot);
    }

    public async Task<AppInvocation> Invoke(InvokeApp request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Snapshot.Invocations.Find(item => item.Id == request.InvocationId) is { } existing) { return existing; }
        RequireInstalled();
        if (!Snapshot.Operations.Any(operation => operation.Name == request.Operation))
        {
            throw new ArgumentException(
                $"{request.Operation} is not an operation of {Snapshot.Revision!.Package}. It offers: {string.Join(", ", Snapshot.Operations.Select(operation => operation.Name))}.");
        }
        if (request.Input is not { Length: <= MaxPayloadLength }) { throw new ArgumentException("An invocation input is at most 64 KiB."); }
        var invocation = new AppInvocation(request.InvocationId, request.Operation, request.Input, InvocationStatus.Pending, null, null, clock.GetUtcNow(), null);
        var invocations = new List<AppInvocation>(Snapshot.Invocations) { invocation };
        while (invocations.Count > MaxInvocations)
        {
            var oldest = invocations.FindIndex(item => item.Status != InvocationStatus.Pending);
            if (oldest < 0) { throw new InvalidOperationException($"{MaxInvocations} invocations are still waiting for the app's behavior."); }
            invocations.RemoveAt(oldest);
        }
        await Save(Snapshot with { Invocations = invocations }, new AppInvoked(invocation.Id, invocation.Operation, invocation.Input));
        return invocation;
    }

    public async Task<AppInvocation> Respond(AppResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var index = Snapshot.Invocations.FindIndex(item => item.Id == response.InvocationId);
        if (index < 0) { throw new KeyNotFoundException($"No invocation {response.InvocationId}."); }
        var invocation = Snapshot.Invocations[index];
        if (invocation.Status != InvocationStatus.Pending) { return invocation; }
        if (response.Output is { Length: > MaxPayloadLength } || response.Error is { Length: > MaxPayloadLength })
        { throw new ArgumentException("An invocation output is at most 64 KiB."); }
        var completed = invocation with
        {
            Status = response.Error is null ? InvocationStatus.Completed : InvocationStatus.Failed,
            Output = response.Output,
            Error = response.Error,
            CompletedAt = clock.GetUtcNow(),
        };
        var invocations = new List<AppInvocation>(Snapshot.Invocations) { [index] = completed };
        await Save(Snapshot with { Invocations = invocations }, new AppInvocationCompleted(completed));
        return completed;
    }

    public Task<AppInvocation> ReadInvocation(Guid invocationId)
        => Snapshot.Invocations.Find(item => item.Id == invocationId) is { } invocation
            ? Task.FromResult(invocation)
            : throw new KeyNotFoundException($"No invocation {invocationId}.");

    public Task<IReadOnlyList<AppInvocation>> Pending()
        => Task.FromResult<IReadOnlyList<AppInvocation>>(Snapshot.Invocations.Where(item => item.Status == InvocationStatus.Pending).ToArray());

    private async Task Deploy(string programKey, Guid operationId, CodeArtifactRef artifact, IReadOnlyDictionary<string, string> settings)
    {
        var program = GrainFactory.GetGrain<IBehaviorProgram>(programKey);
        var current = await program.Read();
        await program.Deploy(new DeployBehavior(current.Revision, operationId, artifact, Configuration(settings)));
    }

    // The behavior reads Behavior:App to find this neuron and Behavior:Settings:{name} for each setting.
    private string Configuration(IReadOnlyDictionary<string, string> settings)
    {
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["Behavior__App"] = this.GetPrimaryKeyString() };
        foreach (var (name, value) in settings) { values["Behavior__Settings__" + name] = value; }
        return JsonSerializer.Serialize(values);
    }

    // Deleted programs cannot be redeployed, so every installation runs under a key of its own.
    private string ProgramKey(int installation)
        => "app-behavior-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(this.GetPrimaryKeyString() + "\0" + installation)));

    private static Dictionary<string, string> Resolve(IReadOnlyList<PackageSetting> declared, IReadOnlyDictionary<string, string> current, IReadOnlyDictionary<string, string> supplied)
    {
        ArgumentNullException.ThrowIfNull(supplied);
        var resolved = declared.ToDictionary(setting => setting.Name, setting => current.TryGetValue(setting.Name, out var chosen) ? chosen : setting.DefaultValue, StringComparer.Ordinal);
        foreach (var (name, value) in supplied)
        {
            var setting = declared.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"{name} is not a setting of this package. It declares: {string.Join(", ", declared.Select(item => item.Name))}.");
            if (value is not { Length: <= MaxSettingLength }) { throw new ArgumentException($"{setting.Name} is at most {MaxSettingLength} characters."); }
            resolved[setting.Name] = value;
        }
        return resolved;
    }

    private void RequireInstalled()
    {
        if (Snapshot.Status != AppStatus.Installed) { throw new InvalidOperationException("The app is not installed."); }
    }

    private bool Replay(Guid operationId, object request)
    {
        var receipt = Snapshot.Receipts.Find(item => item.OperationId == operationId);
        if (receipt is null) { return false; }
        return receipt.RequestHash == PackageHash.Of(request)
            ? true
            : throw new InvalidOperationException($"Operation {operationId} was already used for a different change.");
    }

    private List<OperationReceipt> Receipted(Guid operationId, object request)
    {
        var receipts = new List<OperationReceipt>(Snapshot.Receipts) { new(operationId, PackageHash.Of(request), "") };
        if (receipts.Count > PackageRules.MaxReceipts) { receipts.RemoveAt(0); }
        return receipts;
    }

    private Task Persist(AppState next) => Save(next, new AppChanged(Describe(next)));

    private AppSnapshot Describe(AppState state) => new(
        state.Status,
        state.Revision,
        new Dictionary<string, string>(state.Settings),
        state.Operations.ToArray(),
        state.Installation == 0 ? null : ProgramKey(state.Installation));
}
