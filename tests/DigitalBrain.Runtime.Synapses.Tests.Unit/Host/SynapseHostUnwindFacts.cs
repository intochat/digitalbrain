using DigitalBrain.Runtime.Synapses.Tests.Unit.Compile;
using DigitalBrain.Spi;

namespace DigitalBrain.Runtime.Synapses.Tests.Unit.Host;

public sealed class SynapseHostUnwindFacts
{
    private const string Key = "Me.Budget";

    private static readonly BrainId Brain = NeuronId.Parse("uw/x/y").Brain;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SynapseSource Source() => new(Key, new Dictionary<string, string> { ["Budget.cs"] = SynapseCompilerFacts.Budget }, null);

    private static SynapseHost Host(SynapseLoader loader, Registrar registrar, ISourceStore store, bool installThrows) =>
        new(
            new SynapseCompiler(ReferenceSet.FromDefaultContext()),
            loader,
            registrar,
            new SynapseCatalogSource(SynapseCatalog.Scan()),
            _ => new Installs(installThrows),
            store,
            new Listeners());

    [Fact]
    public async Task AnInstallThatThrowsDeactivatesAndReleasesTheLoad()
    {
        var loader = new SynapseLoader();
        var registrar = new Registrar();
        await using var host = Host(loader, registrar, new Store(), installThrows: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.Deploy(Brain, Source(), new Dictionary<string, IReadOnlyDictionary<string, string>>(), Ct));

        Assert.Equal(["activate", "deactivate"], registrar.Calls);
        Assert.Empty(host.Leaked);
        Assert.Empty(await host.List(Brain, Ct));
        Assert.True(await loader.RetryUnload(Key, host.Compiler.Compile(Source()).Hash));
    }

    [Fact]
    public async Task AnAdoptThatFailsDeactivatesBeforeTheUnload()
    {
        var loader = new SynapseLoader();
        var registrar = new Registrar();
        var store = new Store();
        var build = new SynapseCompiler(ReferenceSet.FromDefaultContext()).Compile(Source());
        Assert.True(build.Success);
        var record = new SourceRecord(Key, Source().Files, null, build.Hash, build.AssemblyName, build.Assembly, build.Symbols, ["week"], DateTimeOffset.UtcNow);
        store.Records.Add(record);
        var host = Host(loader, registrar, store, installThrows: false);
        await host.DisposeAsync();
        registrar.StillLoaded = () => loader.TryGetLive(Key, build.Hash, out _);
        var restorer = new SynapseRestorer(loader, registrar, store, new SynapseCatalogSource(SynapseCatalog.Scan()), host);

        await Assert.ThrowsAsync<ObjectDisposedException>(() => restorer.TryRestore(Brain, "week", Ct));

        Assert.Equal(["activate", "deactivate"], registrar.Calls);
        Assert.False(loader.TryGetLive(Key, build.Hash, out _));
        Assert.True(await loader.RetryUnload(Key, build.Hash));
    }

    private sealed class Registrar : ISynapseRegistrar
    {
        // Set by a fact: Deactivate must see the context still loaded.
        public Func<bool>? StillLoaded { get; set; }

        public List<string> Calls { get; } = [];

        public Task Activate(BrainId brain, LoadedSource source, CancellationToken ct)
        {
            Calls.Add("activate");
            return Task.CompletedTask;
        }

        public Task Deactivate(BrainId brain, string sourceKey, IReadOnlyList<string> aliases, CancellationToken ct)
        {
            Assert.True(StillLoaded?.Invoke() ?? true, "Deactivate ran after the unload");
            Calls.Add("deactivate");
            return Task.CompletedTask;
        }
    }

    private sealed class Installs(bool throws) : IInstalls
    {
        public Task<InstallOutcome> Install(InstallRequest request, CancellationToken ct) =>
            throws ? throw new InvalidOperationException("install failed") : Task.FromResult(new InstallOutcome(InstallStatus.Installed, "", null));

        public Task Retire(string alias, CancellationToken ct) => Task.CompletedTask;

        public Task CancelPending(string alias, CancellationToken ct) => Task.CompletedTask;

        public Task<InstallRecord?> Describe(string alias, CancellationToken ct) => Task.FromResult<InstallRecord?>(null);

        public Task<IReadOnlyList<InstallRecord>> List(CancellationToken ct) => Task.FromResult<IReadOnlyList<InstallRecord>>([]);

        public Task<IReadOnlyList<PendingInstall>> Pending(CancellationToken ct) => Task.FromResult<IReadOnlyList<PendingInstall>>([]);
    }

    private sealed class Store : ISourceStore
    {
        public List<SourceRecord> Records { get; } = [];

        public Task<SourceRecord?> Get(BrainId brain, string sourceKey, CancellationToken ct) => Task.FromResult(Records.Find(r => r.SourceKey == sourceKey));

        public Task Put(BrainId brain, SourceRecord record, CancellationToken ct) => Task.CompletedTask;

        public Task Stage(BrainId brain, string sourceKey, string hash, CancellationToken ct) => Task.CompletedTask;

        public Task Remove(BrainId brain, string sourceKey, CancellationToken ct) => Task.CompletedTask;

        public Task<IReadOnlyList<SourceRecord>> List(BrainId brain, CancellationToken ct) => Task.FromResult<IReadOnlyList<SourceRecord>>([.. Records]);
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
