using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Journaling.Json;
using Orleans.TestingHost;

namespace DigitalBrain.Tests.TestSupport;

#pragma warning disable ORLEANSEXP005

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class OrleansJournalClusterCollection : ICollectionFixture<OrleansJournalClusterFixture>
{
    public const string Name = "orleans-journal-cluster";
}

public sealed class OrleansJournalClusterFixture : IAsyncLifetime
{
    public InProcessTestCluster Cluster { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var builder = new InProcessTestClusterBuilder(initialSilosCount: 1);
        builder.ConfigureSilo((_, siloBuilder) =>
        {
            siloBuilder
                .AddJournalStorage()
                .UseJsonJournalFormat(JournalJson.Configure)
                .ConfigureServices(services =>
                {
                    services.AddScoped<NeuronJournals>();
                    services.AddSingleton<IJournalStorageProvider, VolatileJournalStorageProvider>();
                });
        });

        Cluster = builder.Build();
        await Cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        await Cluster.DisposeAsync();
    }
}

#pragma warning restore ORLEANSEXP005

