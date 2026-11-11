using DigitalBrain.Runtime.Synapses.Tests.Unit.Compile;
using DigitalBrain.Spi;

namespace DigitalBrain.Runtime.Synapses.Tests.Unit.Host;

public sealed class SourceBindingsFlipFacts
{
    private static readonly BrainId Brain = NeuronId.Parse("fl/x/y").Brain;

    // The weekly cap source plus a second state alias.
    private static readonly string TwoAliases = SynapseCompilerFacts.Budget + """

        [Signal("flip.charging2")]
        public sealed record Charging2(decimal Amount) : Signal;

        [State("week2")]
        public sealed record WeekSpend2(decimal Spent = 0)
        {
            public WeekSpend2() : this(0) { }
        }

        public static class Budget2
        {
            [Synapse(typeof(Charging2), "week2")]
            public static WeekSpend2 Cap2(Charging2 s, WeekSpend2 t) => t with { Spent = t.Spent + s.Amount };
        }
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Dictionary<string, IReadOnlyDictionary<string, string>> Both(string connection) => new()
    {
        ["week"] = new Dictionary<string, string> { ["Db"] = connection },
        ["week2"] = new Dictionary<string, string> { ["Db"] = connection },
    };

    [Fact]
    public async Task AHookThatFlipsMidInstallDoesNotStore()
    {
        var installs = new Installs();
        var store = new Store();
        await using var host = new SynapseHost(
            new SynapseCompiler(ReferenceSet.FromDefaultContext()),
            new SynapseLoader(),
            new Registrar(),
            new SynapseCatalogSource(SynapseCatalog.Scan()),
            _ => installs,
            store,
            new Listeners());
        var source = new SynapseSource("Me.Budget", new Dictionary<string, string> { ["Budget.cs"] = TwoAliases }, null);
        var first = await host.Deploy(Brain, source with { Bindings = Both("a") }, new Dictionary<string, IReadOnlyDictionary<string, string>>(), Ct);
        Assert.Equal(DeployStatus.Deployed, first.Status);
        Assert.Single(store.Puts);

        // Validation passes for both aliases, then the install of the second one refuses.
        installs.RefuseInstall = "week2";
        var again = await host.Deploy(Brain, source with { Bindings = Both("b") }, new Dictionary<string, IReadOnlyDictionary<string, string>>(), Ct);

        Assert.Equal(DeployStatus.Refused, again.Status);
        Assert.Single(store.Puts);
    }

    private sealed class Registrar : ISynapseRegistrar
    {
        public Task Activate(BrainId brain, LoadedSource source, CancellationToken ct) => Task.CompletedTask;

        public Task Deactivate(BrainId brain, string sourceKey, IReadOnlyList<string> aliases, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Installs : IInstalls
    {
        public string? RefuseInstall { get; set; }

        public Task<InstallOutcome> Install(InstallRequest request, CancellationToken ct) =>
            Task.FromResult(request.Alias == RefuseInstall
                ? new InstallOutcome(InstallStatus.Refused, "", "the hook flipped")
                : new InstallOutcome(InstallStatus.Updated, "", null));

        public Task<string?> Validate(InstallRequest request, CancellationToken ct) => Task.FromResult<string?>(null);

        public Task Retire(string alias, CancellationToken ct) => Task.CompletedTask;

        public Task CancelPending(string alias, CancellationToken ct) => Task.CompletedTask;

        public Task<InstallRecord?> Describe(string alias, CancellationToken ct) => Task.FromResult<InstallRecord?>(null);

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
