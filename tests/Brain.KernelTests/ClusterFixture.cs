using Brain.Contracts;
using Brain.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.TestingHost;

namespace Brain.KernelTests;

public sealed class ClusterFixture : IDisposable
{
    public TestCluster Cluster { get; }

    public ClusterFixture()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<SiloConfigurator>();
        Cluster = builder.Build();
        Cluster.Deploy();
    }

    public string OwnerSession => "owner|actor/test|session/t";

    public string AddressKey(string kind, string id) =>
        new NeuronAddress("owner", "actor/test", $"{kind}/{id}").ToGrainKey();

    public INeuron Neuron(string kind, string id) =>
        Cluster.GrainFactory.GetGrain<INeuron>(AddressKey(kind, id));

    public void Dispose() => Cluster.StopAllSilos();

    private sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            var storageProvider = new VolatileJournalStorageProvider();
            siloBuilder.AddJournalStorage();
            siloBuilder.Services.AddSingleton<IJournalStorageProvider>(storageProvider);
            siloBuilder.AddBrainKernel(new TestKind());
        }
    }
}
