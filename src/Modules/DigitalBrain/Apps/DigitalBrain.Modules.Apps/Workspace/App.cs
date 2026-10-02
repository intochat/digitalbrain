using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Apps.Signals;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Core.Enforcement;
using DigitalBrain.Microsoft.CSharp;
using DigitalBrain.Postgres;
using Orleans.Runtime;

namespace DigitalBrain.Apps;

[GrainType("apps.app")]
internal sealed class App(
    [PersistentState("app", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<AppState> store,
    TimeProvider clock, AppRequirements requirements, ModuleInventory modules)
    : Neuron<AppState>(store), IApp
{
    private const int MaxInvocations = 256;
    private const int MaxSettingLength = 4096;
    private const int MaxPayloadLength = 64 * 1024;

    public Task<AppSnapshot> Read() => Task.FromResult(Describe(Snapshot));

    public async Task<AppSnapshot> AbandonStorage(AbandonAppStorage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Replay(request.OperationId, request)) { return Describe(Snapshot); }
        if (Snapshot.PendingUninstall is null)
        { throw new InvalidOperationException("Storage can only be abandoned for a pending uninstall."); }
        if (request.OperationId == Snapshot.PendingUninstall.OperationId)
        { throw new InvalidOperationException("Abandoning storage requires its own operation ID."); }
        if (Snapshot.PendingAbandonStorage is null)
        { await Persist(Snapshot with { PendingAbandonStorage = request }); }
        await CompleteUninstall();
        return Describe(Snapshot);
    }

    public async Task<AppSnapshot> Install(InstallApp request)
    {
        ArgumentNullException.ThrowIfNull(request);
        await Resume();
        if (Replay(request.OperationId, request)) { return Describe(Snapshot); }
        if (Snapshot.Status == AppStatus.Installed)
        { throw new InvalidOperationException($"{Snapshot.Revision!.Package} is already installed here; configure or upgrade it instead."); }
        var revision = await GrainFactory.GetGrain<IPackage>(request.Revision.Package.ToString()).ReadRevision(request.Revision.Revision);
        requirements.Check(revision.Content);
        var settings = Resolve(revision.Content.Manifest.Settings, new Dictionary<string, string>(), request.Settings);
        var accounts = ResolveAccounts(revision.Content.Manifest.Accounts ?? [], request.Accounts ?? new Dictionary<string, string>());
        var runtime = revision.Content.Manifest.RuntimeName;
        var generation = Snapshot.ProgramGeneration + 1;
        var programs = runtime == PackageManifest.CSharpRuntime ? revision.Content.Programs().Keys.ToArray() : [];
        await DeployAndPersist(Snapshot with
        {
            Status = AppStatus.Installed,
            Runtime = runtime,
            Revision = request.Revision,
            Declared = [.. revision.Content.Manifest.Settings],
            Settings = settings,
            Accounts = accounts,
            Operations = [.. revision.Content.Manifest.Operations],
            ProgramGeneration = generation,
            ScriptPaths = programs,
            Receipts = Receipted(request.OperationId, request),
            StorageFiles = programs.Select(path => FileKey(generation, path)).ToArray(),
            StorageHistoryKnown = true,
            PostgresScopes = AppRequirements.ReferencesAssembly(revision.Content, typeof(IPostgresTable).Assembly.GetName().Name!),
            LifecycleReceipts = [.. Snapshot.LifecycleReceipts, new(request.OperationId, CommandHash(request), "")],
        });
        return Describe(Snapshot);
    }

    public async Task<AppSnapshot> Configure(ConfigureApp request)
    {
        ArgumentNullException.ThrowIfNull(request);
        await Resume();
        if (Replay(request.OperationId, request)) { return Describe(Snapshot); }
        RequireInstalled();
        var settings = Resolve(Snapshot.Declared, Snapshot.Settings, request.Settings);
        var revision = await GrainFactory.GetGrain<IPackage>(Snapshot.Revision!.Package.ToString()).ReadRevision(Snapshot.Revision.Revision);
        requirements.Check(revision.Content);
        var selected = new Dictionary<string, string>(Snapshot.Accounts);
        foreach (var (slot, account) in request.Accounts ?? new Dictionary<string, string>()) { selected[slot] = account; }
        var accounts = ResolveAccounts(revision.Content.Manifest.Accounts ?? [], selected);
        await BackfillStorageFiles();
        var programs = Snapshot.RunsScript ? revision.Content.Programs().Keys.ToArray() : [];
        await DeployAndPersist(Snapshot with
        {
            Settings = settings,
            Accounts = accounts,
            ProgramGeneration = Snapshot.ProgramGeneration + 1,
            ScriptPaths = programs,
            Receipts = Receipted(request.OperationId, request),
            StorageFiles = [.. Snapshot.StorageFiles, .. programs.Select(path => FileKey(Snapshot.ProgramGeneration + 1, path))]
        });
        return Describe(Snapshot);
    }

    public async Task<AppSnapshot> Upgrade(UpgradeApp request)
    {
        ArgumentNullException.ThrowIfNull(request);
        await Resume();
        if (Replay(request.OperationId, request)) { return Describe(Snapshot); }
        RequireInstalled();
        var installed = Snapshot.Revision!.Package;
        if (request.Revision.Package != installed)
        { throw new ArgumentException($"This app runs {installed}. Install {request.Revision.Package} as its own app to try it."); }
        var revision = await GrainFactory.GetGrain<IPackage>(installed.ToString()).ReadRevision(request.Revision.Revision);
        requirements.Check(revision.Content);
        // Keep what the installer chose for settings the new revision still declares.
        var declared = revision.Content.Manifest.Settings;
        var kept = Snapshot.Settings.Where(pair => declared.Any(setting => setting.Name == pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);
        var settings = Resolve(declared, kept, new Dictionary<string, string>());
        var accounts = ResolveAccounts(revision.Content.Manifest.Accounts ?? [], request.Accounts ?? Snapshot.Accounts);
        var runtime = revision.Content.Manifest.RuntimeName;
        await BackfillStorageFiles();
        var programs = runtime == PackageManifest.CSharpRuntime ? revision.Content.Programs().Keys.ToArray() : [];
        await DeployAndPersist(Snapshot with
        {
            Runtime = runtime,
            Revision = request.Revision,
            ProgramGeneration = Snapshot.ProgramGeneration + 1,
            Declared = [.. declared],
            Settings = settings,
            Accounts = accounts,
            Operations = [.. revision.Content.Manifest.Operations],
            ScriptPaths = programs,
            Receipts = Receipted(request.OperationId, request),
            StorageFiles = [.. Snapshot.StorageFiles, .. programs.Select(path => FileKey(Snapshot.ProgramGeneration + 1, path))],
            PostgresScopes = Snapshot.PostgresScopes || AppRequirements.ReferencesAssembly(revision.Content, typeof(IPostgresTable).Assembly.GetName().Name!),
        });
        return Describe(Snapshot);
    }

    public async Task<AppSnapshot> Uninstall(UninstallApp request)
    {
        ArgumentNullException.ThrowIfNull(request);
        await Resume();
        if (Replay(request.OperationId, request)) { return Describe(Snapshot); }
        if (Snapshot.Status != AppStatus.Installed)
        {
            // Even a no-op must remain a no-op when delivery repeats after a later install.
            await Persist(Snapshot with { LifecycleReceipts = [.. Snapshot.LifecycleReceipts, new(request.OperationId, CommandHash(request), "")] });
            return Describe(Snapshot);
        }
        await BackfillStorageFiles();
        await Persist(Snapshot with { PendingUninstall = request });
        await CompleteUninstall();
        return Describe(Snapshot);
    }

    private async Task CompleteUninstall()
    {
        var request = Snapshot.PendingUninstall!;
        var abandon = Snapshot.PendingAbandonStorage;
        var postgresAvailable = modules.ContractAssemblies().Contains(typeof(IPostgresTable).Assembly);
        if (abandon is null && Snapshot.PostgresScopes && !postgresAvailable) { throw new InvalidOperationException(AppRequirementsException.RestorePostgres); }
        foreach (var file in Snapshot.StorageFiles) { await GrainFactory.GetGrain<ICSharpFile>(file).Delete(); }
        if (abandon is null && postgresAvailable)
        {
            await GrainFactory.GetGrain<IPostgresStorageMigration>("table-owners-v1").Ensure();
            var brain = BrainScope.CurrentId();
            foreach (var file in Snapshot.StorageFiles)
            { await GrainFactory.GetGrain<IPostgresTables>(JsonSerializer.Serialize(new[] { brain, file })).Retire(); }
        }
        var now = clock.GetUtcNow();
        var invocations = Snapshot.Invocations
            .Select(item => item.Status == InvocationStatus.Pending ? item with { Status = InvocationStatus.Failed, Error = "The app was uninstalled.", CompletedAt = now } : item)
            .ToList();
        OperationReceipt[] completed = abandon is null ? [] : [new(abandon.OperationId, CommandHash(abandon), "Storage left behind")];
        await Persist(Snapshot with
        {
            Status = AppStatus.Uninstalled,
            Invocations = invocations,
            Receipts = Receipted(request.OperationId, request),
            PendingUninstall = null,
            PendingAbandonStorage = null,
            AbandonedStorage = abandon is null ? Snapshot.AbandonedStorage : [.. Snapshot.AbandonedStorage, new(abandon.OperationId, Snapshot.StorageFiles.ToArray())],
            LifecycleReceipts = [.. Snapshot.LifecycleReceipts, new(request.OperationId, CommandHash(request), ""), .. completed]
        });
    }

    public async Task<AppInvocation> Invoke(InvokeApp request)
    {
        ArgumentNullException.ThrowIfNull(request);
        await Resume();
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
            if (oldest < 0) { throw new InvalidOperationException($"{MaxInvocations} invocations are still waiting for the app's script."); }
            invocations.RemoveAt(oldest);
        }
        await Save(Snapshot with { Invocations = invocations }, new AppInvoked(invocation.Id, invocation.Operation, invocation.Input));
        if (!Snapshot.RunsScript)
        { await GrainFactory.GetGrain<IAppRuntimeWorker>(0).Answer(this.GetPrimaryKeyString(), Snapshot.Revision!, invocation); }
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

    // Every deployment gets fresh files, so a command retried after a lost save rewrites and restarts
    // the same files instead of depending on what the previous generation left behind.
    // Only a csharp app has programs to run; every other runtime answers through IAppRuntimeWorker.
    private async Task<string[]> Deploy(string runtime, int generation, PackageContent content, IReadOnlyDictionary<string, string> settings, IReadOnlyDictionary<string, string> accounts)
    {
        if (runtime != PackageManifest.CSharpRuntime) { return []; }
        var programs = content.Programs();
        foreach (var (path, source) in programs)
        {
            var file = GrainFactory.GetGrain<ICSharpFile>(FileKey(generation, path));
            await file.Write(source);
            await file.Configure(Configuration(settings, accounts));
            await file.Start();
        }
        return [.. programs.Keys];
    }

    // Deleting an already deleted file is harmless, so retries need no bookkeeping.
    private async Task Retire(string runtime, int generation)
    {
        if (runtime != PackageManifest.CSharpRuntime) { return; }
        foreach (var path in Snapshot.ScriptPaths)
        {
            await GrainFactory.GetGrain<ICSharpFile>(FileKey(generation, path)).Delete();
        }
    }

    private async Task Resume()
    {
        if (Snapshot.PendingDeployment is not null) { await CompleteDeployment(); }
        if (Snapshot.PendingUninstall is not null) { await CompleteUninstall(); }
    }

    private async Task DeployAndPersist(AppState target)
    {
        await Persist(Snapshot with { PendingDeployment = new(target) });
        await CompleteDeployment();
    }

    private async Task CompleteDeployment()
    {
        var target = Snapshot.PendingDeployment!.Target;
        var revision = await GrainFactory.GetGrain<IPackage>(target.Revision!.Package.ToString()).ReadRevision(target.Revision.Revision);
        await Retire(Snapshot.Runtime, Snapshot.ProgramGeneration);
        await Deploy(target.Runtime, target.ProgramGeneration, revision.Content, target.Settings, target.Accounts);
        await Persist(target);
    }

    private async Task BackfillStorageFiles()
    {
        if (Snapshot.StorageHistoryKnown) { return; }
        var paths = new HashSet<string>(Snapshot.ScriptPaths, StringComparer.Ordinal) { PackageContent.SourcePath };
        var postgres = Snapshot.PostgresScopes;
        if (Snapshot.Revision is { } installed)
        {
            var package = GrainFactory.GetGrain<IPackage>(installed.Package.ToString());
            var remaining = new Stack<string>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            remaining.Push(installed.Revision);
            while (remaining.TryPop(out var id))
            {
                if (!visited.Add(id)) { continue; }
                var revision = await package.ReadRevision(id);
                paths.UnionWith(revision.Content.Programs().Keys);
                postgres |= AppRequirements.ReferencesAssembly(revision.Content, typeof(IPostgresTable).Assembly.GetName().Name!);
                foreach (var parent in revision.Parents) { remaining.Push(parent); }
            }
        }
        var files = Enumerable.Range(1, Snapshot.ProgramGeneration).SelectMany(generation => paths.Select(path => FileKey(generation, path)));
        await Persist(Snapshot with { StorageFiles = files.Distinct(StringComparer.Ordinal).ToArray(), StorageHistoryKnown = true, PostgresScopes = postgres });
    }

    // The script reads brain.Setting("App") to find this neuron, brain.Setting(name) for each setting
    // and brain.Setting("Account__" + slot) for each connected account.
    private Dictionary<string, string> Configuration(IReadOnlyDictionary<string, string> settings, IReadOnlyDictionary<string, string> accounts)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["App"] = this.GetPrimaryKeyString() };
        foreach (var (name, value) in settings) { values[name] = value; }
        foreach (var (name, id) in accounts) { values["Account__" + name] = id; }
        return values;
    }

    private static Dictionary<string, string> ResolveAccounts(IReadOnlyList<PackageAccount> declared, IReadOnlyDictionary<string, string> selected)
    {
        if (selected.Keys.Any(name => !declared.Any(slot => slot.Name == name)))
        { throw new ArgumentException("The installation contains an undeclared account slot."); }
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var slot in declared)
        {
            if (!selected.TryGetValue(slot.Name, out var id) || string.IsNullOrWhiteSpace(id))
            { throw new ArgumentException($"Choose an account for {slot.Name} ({slot.Source})."); }
            result.Add(slot.Name, id);
        }
        return result;
    }

    private string FileKey(int generation, string path)
        => "app-csharp-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(this.GetPrimaryKeyString() + "\0" + generation + "\0" + path)));

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
        var receipt = Snapshot.LifecycleReceipts.FirstOrDefault(item => item.OperationId == operationId)
            ?? Snapshot.Receipts.Find(item => item.OperationId == operationId);
        if (receipt is null) { return false; }
        return receipt.RequestHash == CommandHash(request)
            ? true
            : throw new InvalidOperationException($"Operation {operationId} was already used for a different change.");
    }

    private List<OperationReceipt> Receipted(Guid operationId, object request)
    {
        var receipts = new List<OperationReceipt>(Snapshot.Receipts) { new(operationId, CommandHash(request), "") };
        if (receipts.Count > PackageRules.MaxReceipts) { receipts.RemoveAt(0); }
        return receipts;
    }

    // Settings are a set: the same keys sent in another order are the same command.
    private static string CommandHash(object request) => PackageHash.Of(request switch
    {
        InstallApp install => install with
        {
            Settings = new SortedDictionary<string, string>(install.Settings.ToDictionary(), StringComparer.Ordinal),
            Accounts = new SortedDictionary<string, string>((install.Accounts ?? new Dictionary<string, string>()).ToDictionary(), StringComparer.Ordinal)
        },
        ConfigureApp configure => configure with
        {
            Settings = new SortedDictionary<string, string>(configure.Settings.ToDictionary(), StringComparer.Ordinal),
            Accounts = new SortedDictionary<string, string>((configure.Accounts ?? new Dictionary<string, string>()).ToDictionary(), StringComparer.Ordinal)
        },
        UpgradeApp upgrade => upgrade with { Accounts = new SortedDictionary<string, string>((upgrade.Accounts ?? new Dictionary<string, string>()).ToDictionary(), StringComparer.Ordinal) },
        _ => request,
    });

    private Task Persist(AppState next) => Save(next, new AppChanged(Describe(next)));

    private AppSnapshot Describe(AppState state) => new(
        state.Status,
        state.Revision,
        new Dictionary<string, string>(state.Settings),
        state.Operations.ToArray(),
        state.ProgramGeneration == 0 || !state.RunsScript
            ? []
            : state.ScriptPaths.Select(path => FileKey(state.ProgramGeneration, path)).ToArray(),
        new Dictionary<string, string>(state.Accounts), state.PendingUninstall is not null, state.AbandonedStorage.ToArray());
}
