using DigitalBrain.TestKit;
using DigitalBrain.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;

namespace DigitalBrain.Tests.TestSupport;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class OrdinaryPersistenceClusterCollection : ICollectionFixture<OrdinaryPersistenceClusterFixture>
{
    public const string Name = "orleans-ordinary-persistence-cluster";
}

public sealed class OrdinaryPersistenceClusterFixture : IAsyncLifetime
{
    public InProcessTestCluster Cluster { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var builder = new InProcessTestClusterBuilder(initialSilosCount: 1);
        builder.ConfigureSilo((_, siloBuilder) =>
        {
            siloBuilder
                .AddMemoryGrainStorageAsDefault()
                .ConfigureServices(services =>
                {
                    NeuronTestKernelConfigurator.AddRuntimeStateProtection(services);
                    services.Configure<NeuronLifecycleOptions>(options =>
                    {
                        options.MaximumRetainedSynapsesPerDirection = 8;
                        options.MaximumTimelinePlaintextBytes = 8 * 1024;
                    });
                });
        });

        Cluster = builder.Build();
        await Cluster.DeployAsync();
    }

    public Task DisposeAsync() => Cluster.DisposeAsync().AsTask();
}
