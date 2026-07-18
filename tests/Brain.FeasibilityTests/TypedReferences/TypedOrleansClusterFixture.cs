using Orleans.TestingHost;

namespace Brain.FeasibilityTests.TypedReferences;

public sealed class TypedOrleansClusterFixture : IDisposable
{
    public TestCluster Cluster { get; }

    public TypedOrleansClusterFixture()
    {
        var builder = new TestClusterBuilder();
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
        }
    }
}
