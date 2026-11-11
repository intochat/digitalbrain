using DigitalBrain.Runtime.Synapses.Tests.Unit.Compile;
using DigitalBrain.Kernel;
using DigitalBrain.Units;

namespace DigitalBrain.Runtime.Synapses.Tests.Unit.Host;

// A rebind that waits for the person is dropped only after its approved bindings are stored, so a failed store keeps it pending.
public sealed class ResolveRebindFacts
{
    private const string Key = "Me.Budget";

    private static readonly BrainId Brain = NeuronId.Parse("rr/x/y").Brain;

    private static readonly string WithSlot = SynapseCompilerFacts.Budget.Replace(
        "WeeklyCap(Charging s, WeekSpend t)",
        "WeeklyCap(Charging s, WeekSpend t, [Slot] DigitalBrain.Runtime.Synapses.Tests.Unit.Host.IStageRead Db)",
        StringComparison.Ordinal);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Dictionary<string, IReadOnlyDictionary<string, string>> Bind(string connection) =>
        new() { ["week"] = new Dictionary<string, string> { ["Db"] = connection } };

    [Fact]
    public async Task AFailedStoreKeepsTheApprovedRebindForTheNextResolution()
    {
        var store = new Store();
        var listeners = new Listeners();
        var installs = new Installs();
        var outstanding = new List<Task>();
        var catalog = new SynapseCatalogSource(SynapseCatalog.Scan());
        await using var host = new SynapseHost(
            new SynapseCompiler(ReferenceSet.FromDefaultContext()), new SynapseLoader(), new Registrar(catalog), catalog, _ => installs, store, listeners,
            track: outstanding.Add);
        var source = new SynapseSource(Key, new Dictionary<string, string> { ["Budget.cs"] = WithSlot }, null);
        Assert.Equal(DeployStatus.Deployed, (await host.Deploy(Brain, source, Bind("conn-1"), Ct)).Status);

        installs.Ask = true;
        var again = await host.Deploy(Brain, source, Bind("conn-2"), Ct);
        Assert.Equal(InstallStatus.Pending, again.Installs["week"].Status);
        Assert.Equal("conn-1", store.Last!.Bindings!["week"]["Db"]);

        // The person approves, but the store refuses the write once: the pending rebind must survive it.
        store.FailNextPut = true;
        await listeners.Raise(new Installed("week", "needs"));
        await Task.WhenAll(outstanding);
        Assert.Equal("conn-1", store.Last!.Bindings!["week"]["Db"]);

        await listeners.Raise(new Installed("week", "needs"));
        await Task.WhenAll(outstanding);

        Assert.Equal("conn-2", store.Last!.Bindings!["week"]["Db"]);
    }

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
        private readonly List<SourceRecord> puts = [];

        public bool FailNextPut { get; set; }

        public SourceRecord? Last => puts.LastOrDefault();

        public Task<SourceRecord?> Get(BrainId brain, string sourceKey, CancellationToken ct) => Task.FromResult(puts.LastOrDefault(r => r.SourceKey == sourceKey));

        public Task Put(BrainId brain, SourceRecord record, CancellationToken ct)
        {
            if (FailNextPut)
            {
                FailNextPut = false;
                throw new InvalidOperationException("store unavailable");
            }

            puts.Add(record);
            return Task.CompletedTask;
        }

        public Task Stage(BrainId brain, string sourceKey, string hash, CancellationToken ct) => Task.CompletedTask;

        public Task Remove(BrainId brain, string sourceKey, CancellationToken ct) => Task.CompletedTask;

        public Task<IReadOnlyList<SourceRecord>> List(BrainId brain, CancellationToken ct) => Task.FromResult<IReadOnlyList<SourceRecord>>([.. puts]);
    }

    // Keeps the host's handlers so a fact can raise an install resolution as the installs neuron would.
    private sealed class Listeners : IClusterListeners
    {
        private readonly List<(Type Type, Func<NeuronId, Signal, CancellationToken, Task> Handler)> handlers = [];

        public Task<IAsyncDisposable> Listen<TSignal>(string listener, Func<NeuronId, TSignal, CancellationToken, Task> handler)
            where TSignal : Signal
        {
            handlers.Add((typeof(TSignal), (id, signal, ct) => handler(id, (TSignal)signal, ct)));
            return Task.FromResult<IAsyncDisposable>(new Nop());
        }

        public Task Raise(Signal signal) =>
            Task.WhenAll(handlers.Where(h => h.Type == signal.GetType()).Select(h => h.Handler(new NeuronId(Brain, "synapse.installs", "default"), signal, CancellationToken.None)));

        private sealed class Nop : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
