using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Llm;
using DigitalBrain.Kernel.Runtime;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.OrleansTests.TestSupport;

[Trait("Category", "cluster")]
public abstract class NeuronTestBase : IAsyncLifetime
{
    private InProcessTestCluster? _cluster;

    protected virtual void ConfigureSilo(ISiloBuilder builder) { }
    protected virtual void ConfigureClient(IClientBuilder builder) { }
    protected virtual short InitialSilosCount => 1;
    protected TestDigitalBrainCluster Cluster => new(_cluster!);
    public TGrain Grain<TGrain>(string key) where TGrain : IGrainWithStringKey =>
        _cluster!.Client.GetGrain<TGrain>(key);

    public async Task InitializeAsync()
    {
        var builder = new InProcessTestClusterBuilder(InitialSilosCount);
        builder.ConfigureSilo((_, silo) =>
        {
            silo.Configure<SiloMessagingOptions>(options => options.ResponseTimeout = TimeSpan.FromMinutes(2));
            silo.AddMemoryGrainStorageAsDefault();
            silo.ConfigureServices(services =>
            {
                var keyRing = new RuntimeStateKeyRing(
                    1,
                    new Dictionary<int, byte[]> { [1] = Enumerable.Repeat((byte)0x31, 32).ToArray() },
                    Enumerable.Repeat((byte)0x53, 32).ToArray());
                services.AddSingleton(keyRing);
                services.AddSingleton<IRuntimeStateKeyRing>(keyRing);
                services.AddSingleton(new EncryptedRuntimeStateProtector(keyRing));
                services.AddSingleton<IScopedChatClientFactory, NoOpScopedChatClientFactory>();
                services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(new NoOpEmbeddingGenerator());
            });
            ConfigureSilo(silo);
        });
        builder.ConfigureClient(client =>
        {
            client.Configure<ClientMessagingOptions>(options => options.ResponseTimeout = TimeSpan.FromMinutes(2));
            ConfigureClient(client);
        });
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        if (_cluster is not null) await _cluster.DisposeAsync();
    }

    private sealed class NoOpScopedChatClientFactory : IScopedChatClientFactory
    {
        public IChatClient? Create(string provider, string? apiKey) => null;
    }
}

public sealed class TestDigitalBrainCluster(InProcessTestCluster cluster)
{
    public IReadOnlyList<InProcessSiloHandle> Silos => cluster.Silos;
    public IClusterClient Client => cluster.Client;
    public IGrainFactory GrainFactory => cluster.Client;
    public Task DeactivateAsync(IAddressable grain) => cluster.DeactivateAsync(grain);
    public Task StopAllSilosAsync() => cluster.StopAllSilosAsync();
}
