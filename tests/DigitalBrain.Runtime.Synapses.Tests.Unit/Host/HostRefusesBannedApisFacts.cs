using DigitalBrain.Runtime.Synapses.Tests.Unit.Compile;
using DigitalBrain.Spi;

namespace DigitalBrain.Runtime.Synapses.Tests.Unit.Host;

[Capability("stage-read", 1)]
public interface IStageRead
{
    Task<int> Count(CancellationToken ct);
}

public sealed class HostRefusesBannedApisFacts
{
    private const string Key = "Me.Budget";

    private const string NetLeak = "\npublic static class Leak { public static object Client() => new System.Net.Http.HttpClient(); }";

    private static readonly NoBindings None = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Each is appended to the weekly cap source; the analyzer catches most of them, the host must catch all of them.
    public static TheoryData<string, string> Bypasses => new()
    {
        { "System.Net", NetLeak },
        { "System.Activator", "public static class Leak { public static object? Make() => Activator.CreateInstance(Type.GetType(\"System.Net.Http.HttpClient, System.Net.Http\")!); }" },
        { "native import", "public static class Leak { [System.Runtime.InteropServices.DllImport(\"kernel32\")] public static extern int GetTickCount(); }" },
        { "System.Runtime.InteropServices.Marshal", "public static class Leak { public static IntPtr Grab() => System.Runtime.InteropServices.Marshal.AllocHGlobal(4); }" },
        { "Microsoft.CSharp.RuntimeBinder", "public static class Leak { public static object Bind(dynamic x) => x.Anything; }" },
        { "System.Threading.Tasks.TaskFactory", "public static class Leak { public static Task Spawn() => Task.Factory.StartNew(() => { }); }" },
        { "System.Environment", "public static class Leak { public static string? Path() => Environment.GetEnvironmentVariable(\"PATH\"); }" },
    };

    [Fact]
    public void TheCompilerReportsDB0045()
    {
        var build = new SynapseCompiler(ReferenceSet.FromDefaultContext()).Compile(Source(SynapseCompilerFacts.Budget + NetLeak));

        Assert.False(build.Success);
        Assert.Contains(build.Diagnostics, diagnostic => diagnostic.Id == "DB0045");
    }

    [Fact]
    public Task HostRefusesAnAssemblyThatReferencesSystemNet() => Refuses("System.Net.Http.HttpClient", NetLeak);

    [Theory]
    [MemberData(nameof(Bypasses))]
    public Task HostRefusesWhatTheAnalyzerWasNotThereToSee(string named, string leak) => Refuses(named, leak);

    [Fact]
    public async Task AUnitUsingTheAllowedSurfaceDeploys()
    {
        await using var host = Host(new Installs());
        var outcome = await host.Deploy(Brain("ok"), Source(SynapseCompilerFacts.Budget + """

            public sealed record Note(string Text);

            public static class Allowed
            {
                public static async Task<long> Measure(CancellationToken ct)
                {
                    await Task.Delay(1, ct);
                    using var buffer = new System.IO.MemoryStream(new byte[4]);
                    var note = new Note(nameof(Measure));
                    return buffer.Length + note.ToString().Length + (note == new Note("x") ? 1 : 0) + Environment.NewLine.Length;
                }
            }
            """), None, Ct);

        Assert.True(outcome.Status == DeployStatus.Deployed, outcome.Reason);
    }

    [Fact]
    public async Task ARefusedDeployKeepsTheStagedBuild()
    {
        var installs = new Installs();
        await using var host = Host(installs);
        var brain = Brain("st");
        Assert.Equal(DeployStatus.Deployed, (await host.Deploy(brain, Source(SynapseCompilerFacts.Budget), None, Ct)).Status);
        installs.Ask = true;
        var withSlot = SynapseCompilerFacts.Budget.Replace(
            "WeeklyCap(Charging s, WeekSpend t)",
            "WeeklyCap(Charging s, WeekSpend t, [Slot] DigitalBrain.Runtime.Synapses.Tests.Unit.Host.IStageRead Db)",
            StringComparison.Ordinal);
        var staged = await host.Deploy(brain, Source(withSlot), None, Ct);
        Assert.True(staged.Status == DeployStatus.Staged, $"{staged.Status} {staged.Reason} {string.Join(",", staged.Installs.Values)}");

        var refused = await host.Deploy(brain, Source(withSlot + NetLeak), None, Ct);

        Assert.Equal(DeployStatus.Refused, refused.Status);
        Assert.Equal(staged.Hash, Assert.Single(await host.List(brain, Ct)).StagedHash);
    }

    private static async Task Refuses(string named, string leak)
    {
        await using var host = Host(new Installs());
        var source = Source(SynapseCompilerFacts.Budget + "\n" + leak);
        Assert.True(host.Compiler.Compile(source).Success);

        var outcome = await host.Deploy(Brain("ba"), source, None, Ct);

        Assert.Equal(DeployStatus.Refused, outcome.Status);
        Assert.Contains(named, outcome.Reason, StringComparison.Ordinal);
        var diagnostic = Assert.Single(outcome.Diagnostics, d => d.Id == "DB0045");
        Assert.Equal(Key, diagnostic.File);
        Assert.Empty(await host.List(Brain("ba"), Ct));
    }

    private static BrainId Brain(string name) => NeuronId.Parse($"{name}/x/y").Brain;

    private static SynapseSource Source(string text) => new(Key, new Dictionary<string, string> { ["Budget.cs"] = text }, null);

    // The analyzer is left out, so only the host's own check stands in front of the load.
    private static SynapseHost Host(Installs installs)
    {
        var catalog = new SynapseCatalogSource(SynapseCatalog.Scan());
        return new(
            new SynapseCompiler(ReferenceSet.FromDefaultContext(), [], withoutBannedApis: true),
            new SynapseLoader(),
            new Registrar(catalog),
            catalog,
            _ => installs,
            new Store(),
            new Listeners());
    }

    private sealed class NoBindings : Dictionary<string, IReadOnlyDictionary<string, string>>;

    // Merges what it activates, as the runtime registrar does, so the host sees the live needs.
    private sealed class Registrar(SynapseCatalogSource catalog) : ISynapseRegistrar
    {
        public Task Activate(BrainId brain, LoadedSource source, CancellationToken ct)
        {
            catalog.Merge(source.Catalog);
            return Task.CompletedTask;
        }

        public Task Deactivate(BrainId brain, string sourceKey, IReadOnlyList<string> aliases, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Installs : IInstalls
    {
        // Once set, every install waits for the person, so a needs-changing update is staged.
        public bool Ask { get; set; }

        public Task<InstallOutcome> Install(InstallRequest request, CancellationToken ct) =>
            Task.FromResult(Ask ? new InstallOutcome(InstallStatus.Pending, "", "plan-1") : new InstallOutcome(InstallStatus.Updated, "", null));

        public Task<string?> Validate(InstallRequest request, CancellationToken ct) => Task.FromResult<string?>(null);

        public Task Retire(string alias, CancellationToken ct) => Task.CompletedTask;

        public Task CancelPending(string alias, CancellationToken ct) => Task.CompletedTask;

        public Task<InstallRecord?> Describe(string alias, CancellationToken ct) =>
            Task.FromResult<InstallRecord?>(new InstallRecord(alias, "", new Dictionary<string, string>(), [], DateTimeOffset.UnixEpoch));

        public Task<IReadOnlyList<InstallRecord>> List(CancellationToken ct) => Task.FromResult<IReadOnlyList<InstallRecord>>([]);

        public Task<IReadOnlyList<PendingInstall>> Pending(CancellationToken ct) => Task.FromResult<IReadOnlyList<PendingInstall>>([]);
    }

    private sealed class Store : ISourceStore
    {
        public List<SourceRecord> Puts { get; } = [];

        public Task<SourceRecord?> Get(BrainId brain, string sourceKey, CancellationToken ct) => Task.FromResult(Puts.LastOrDefault(r => r.SourceKey == sourceKey));

        public Task Put(BrainId brain, SourceRecord record, CancellationToken ct)
        {
            Puts.Add(record);
            return Task.CompletedTask;
        }

        public Task Stage(BrainId brain, string sourceKey, string hash, CancellationToken ct) => Task.CompletedTask;

        public Task Remove(BrainId brain, string sourceKey, CancellationToken ct) => Task.CompletedTask;

        public Task<IReadOnlyList<SourceRecord>> List(BrainId brain, CancellationToken ct) => Task.FromResult<IReadOnlyList<SourceRecord>>([.. Puts]);
    }

    private sealed class Listeners : IClusterListeners
    {
        public Task<IAsyncDisposable> Listen<TSignal>(string listener, Func<NeuronId, TSignal, CancellationToken, Task> handler)
            where TSignal : Signal => Task.FromResult<IAsyncDisposable>(new Nop());

        private sealed class Nop : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
