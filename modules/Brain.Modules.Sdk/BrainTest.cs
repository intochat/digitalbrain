using Brain.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Journaling;
using Orleans.TestingHost;
using Xunit;

namespace Brain.Modules.Sdk;

public sealed class BrainClusterFixture<TKindsConfigurator> : IDisposable
    where TKindsConfigurator : ISiloConfigurator, new()
{
    public TestCluster Cluster { get; }

    public BrainClusterFixture()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<JournalStorageConfigurator>();
        builder.AddSiloBuilderConfigurator<TKindsConfigurator>();
        Cluster = builder.Build();
        Cluster.Deploy();
    }

    public string OwnerSession => "owner|actor/test|session/t";

    public string AddressKey(string kind, string id) =>
        new NeuronAddress("owner", "actor/test", $"{kind}/{id}").ToGrainKey();

    public INeuron Neuron(string kind, string id) =>
        Cluster.GrainFactory.GetGrain<INeuron>(AddressKey(kind, id));

    public void Dispose() => Cluster.StopAllSilos();

    private sealed class JournalStorageConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            var storageProvider = new VolatileJournalStorageProvider();
            siloBuilder.AddJournalStorage();
            siloBuilder.Services.AddSingleton<IJournalStorageProvider>(storageProvider);
        }
    }
}

public abstract class BrainTest<TKindsConfigurator>(BrainClusterFixture<TKindsConfigurator> fixture)
    : IClassFixture<BrainClusterFixture<TKindsConfigurator>>
    where TKindsConfigurator : ISiloConfigurator, new()
{
    protected TestCluster Cluster => fixture.Cluster;

    protected string OwnerSession => fixture.OwnerSession;

    protected string AddressKey(string kind, string id) => fixture.AddressKey(kind, id);

    protected INeuron Neuron(string kind, string id) => fixture.Neuron(kind, id);
}
