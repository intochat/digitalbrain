using Orleans;
using Orleans.TestingHost;
using Xunit;

namespace Ino.Testing;

/// <summary>
/// Shared per-test-project fixture. One Orleans TestCluster is created per test project
/// and reused across every test class via xunit.v3's ICollectionFixture. Cluster startup
/// (~5-10s) is paid once; per-test reset is in-memory and fast.
///
/// Usage:
///   [Collection(nameof(InoTestCollection))]
///   public sealed class MyTests
///   {
///       private readonly InoTestSiloFixture _fixture;
///       public MyTests(InoTestSiloFixture fixture) { _fixture = fixture; }
///   }
/// </summary>
public sealed class InoTestSiloFixture : IAsyncLifetime
{
    public TestCluster Cluster { get; private set; } = null!;

    public IGrainFactory Grains => Cluster.Client;

    public async ValueTask InitializeAsync()
    {
        var builder = new TestClusterBuilder { Options = { InitialSilosCount = 1 } };
        builder.AddSiloBuilderConfigurator<TestSiloConfigurator>();
        Cluster = builder.Build();
        await Cluster.DeployAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (Cluster is null) return;

        try
        {
            await Cluster.StopAllSilosAsync();
        }
        finally
        {
            await Cluster.DisposeAsync();
        }
    }
}
