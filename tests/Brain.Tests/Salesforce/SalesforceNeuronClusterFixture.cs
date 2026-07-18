using Brain.Kernel;
using DigitalBrain.Salesforce;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Journaling.Json;
using Orleans.Streams;
using Orleans.TestingHost;
using Xunit;

namespace Brain.Tests.Salesforce;

[CollectionDefinition(SalesforceTestCollection.Name, DisableParallelization = true)]
public sealed class SalesforceTestCollection : ICollectionFixture<SalesforceNeuronClusterFixture>
{
    public const string Name = "salesforce-neurons";
}

public sealed class SalesforceNeuronClusterFixture : IDisposable
{
    public static FakeSalesforceMcpClient SharedMcp { get; } = new();

    public TestCluster Cluster { get; }
    public FakeSalesforceMcpClient Mcp => SharedMcp;

    public SalesforceNeuronClusterFixture()
    {
        SharedMcp.Reset();
        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        builder.AddSiloBuilderConfigurator<SiloConfigurator>();
        Cluster = builder.Build();
        Cluster.Deploy();
    }

    public void Dispose()
    {
        Cluster.StopAllSilos();
        Cluster.Dispose();
    }

    private sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder.UseJsonJournalFormat(SalesforceJournalJsonContext.Default);
            siloBuilder.AddJournalStorage();
            siloBuilder.Services.AddSingleton<IJournalStorageProvider>(new VolatileJournalStorageProvider());
            siloBuilder.UseInMemoryReminderService();
            siloBuilder.AddMemoryGrainStorageAsDefault();
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
            siloBuilder.AddMemoryStreams(ReactiveNeuron<SalesforceFeedEvent>.DefaultStreamProviderName, configure =>
            {
                configure.ConfigureStreamPubSub(StreamPubSubType.ExplicitGrainBasedOnly);
            });
            siloBuilder.Services.AddSingleton<ISalesforceMcpClient>(_ => SharedMcp);
        }
    }
}
