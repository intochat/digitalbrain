using DigitalBrain.Core;
using Orleans.Configuration;
using Orleans.Runtime;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.TestKit;

public sealed class TestDigitalBrain(
    Action<ISiloBuilder>? extendSilo = null,
    Action<IClientBuilder>? extendClient = null,
    short initialSilosCount = 1) : IDigitalBrain, IAsyncLifetime
{
    private readonly Action<ISiloBuilder>? _extendSilo = extendSilo;
    private readonly Action<IClientBuilder>? _extendClient = extendClient;
    private readonly short _initialSilosCount = initialSilosCount;
    private InProcessTestCluster? _cluster;
    private static readonly TimeSpan TestClusterResponseTimeout = TimeSpan.FromMinutes(2);

    public TestDigitalBrainCluster Cluster => new(_cluster!);

    public async Task InitializeAsync()
    {
        // Ensure grains and SystemStatus skip heavy MCP / warmup side effects inside cluster tests.
        Environment.SetEnvironmentVariable("DIGITALBRAIN_TEST_MODE", "true");

        var builder = new InProcessTestClusterBuilder(initialSilosCount: _initialSilosCount);
        builder.ConfigureSilo((_, siloBuilder) =>
        {
            ConfigureMessagingTimeout(siloBuilder);
            new NeuronTestKernelConfigurator().Configure(siloBuilder);
            _extendSilo?.Invoke(siloBuilder);
        });

        builder.ConfigureClient(clientBuilder =>
        {
            ConfigureMessagingTimeout(clientBuilder);
            _extendClient?.Invoke(clientBuilder);
        });

        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        if (_cluster is not null)
            await _cluster.DisposeAsync();
    }

    public TGrain Grain<TGrain>(string key) where TGrain : IGrainWithStringKey =>
        Cluster.GrainFactory.GetGrain<TGrain>(key);

    public Task FireAsync<T>(T synapse) where T : Synapse =>
        Grain<INeuron>(synapse.SynapseId.ToString()).DeliverAsync(synapse);

    public Task DeliverAsync<T>(T synapse) where T : Synapse =>
        synapse.Receiver is { } r
            ? Grain<INeuron>(r.Value).DeliverAsync(synapse)
            : throw new InvalidOperationException("DeliverAsync requires synapse.Receiver to be set.");

    private static void ConfigureMessagingTimeout(ISiloBuilder builder) =>
        builder.Configure<MessagingOptions>(options => options.ResponseTimeout = TestClusterResponseTimeout);

    private static void ConfigureMessagingTimeout(IClientBuilder builder) =>
        builder.Configure<MessagingOptions>(options => options.ResponseTimeout = TestClusterResponseTimeout);
}

public sealed class TestDigitalBrainCluster(InProcessTestCluster cluster)
{
    public InProcessTestCluster Inner => cluster;
    public IReadOnlyList<InProcessSiloHandle> Silos => cluster.Silos;
    public IClusterClient Client => cluster.Client;
    public IGrainFactory GrainFactory => cluster.Client;

    public Task DeactivateAsync(IAddressable grain) => cluster.DeactivateAsync(grain);

    public Task StopAllSilosAsync() => cluster.StopAllSilosAsync();
}
