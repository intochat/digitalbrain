using DigitalBrain.Runtime.Synapses.Tests.Unit.Compile;
using DigitalBrain.Spi;

namespace DigitalBrain.Runtime.Synapses.Tests.Unit.Host;

public sealed class HostRefusesBannedApisFacts
{
    private static readonly BrainId Brain = NeuronId.Parse("ba/x/y").Brain;

    private static readonly string Networked = SynapseCompilerFacts.Budget + """

        public static class Leak
        {
            public static object Client() => new System.Net.Http.HttpClient();
        }
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void TheCompilerReportsDB0045()
    {
        var build = new SynapseCompiler(ReferenceSet.FromDefaultContext()).Compile(Source());

        Assert.False(build.Success);
        Assert.Contains(build.Diagnostics, diagnostic => diagnostic.Id == "DB0045");
    }

    [Fact]
    public async Task HostRefusesAnAssemblyThatReferencesSystemNet()
    {
        // The analyzer is left out, so the build succeeds and only the host's own check stands in front of the load.
        var compiler = new SynapseCompiler(ReferenceSet.FromDefaultContext(), [], withoutBannedApis: true);
        Assert.True(compiler.Compile(Source()).Success);
        var loader = new SynapseLoader();
        await using var host = new SynapseHost(
            compiler,
            loader,
            new Registrar(),
            new SynapseCatalogSource(SynapseCatalog.Scan()),
            _ => new Installs(),
            new Store(),
            new Listeners());

        var outcome = await host.Deploy(Brain, Source(), new Dictionary<string, IReadOnlyDictionary<string, string>>(), Ct);

        Assert.Equal(DeployStatus.Refused, outcome.Status);
        Assert.Contains("System.Net.Http.HttpClient", outcome.Reason, StringComparison.Ordinal);
        Assert.Contains(outcome.Diagnostics, diagnostic => diagnostic.Id == "DB0045");
        Assert.Empty(await host.List(Brain, Ct));
    }

    private static SynapseSource Source() => new("Me.Budget", new Dictionary<string, string> { ["Budget.cs"] = Networked }, null);

    private sealed class Registrar : ISynapseRegistrar
    {
        public Task Activate(BrainId brain, LoadedSource source, CancellationToken ct) => Task.CompletedTask;

        public Task Deactivate(BrainId brain, string sourceKey, IReadOnlyList<string> aliases, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Installs : IInstalls
    {
                public Task<InstallOutcome> Install(InstallRequest request, CancellationToken ct) =>
            Task.FromResult(new InstallOutcome(InstallStatus.Updated, "", null));

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
