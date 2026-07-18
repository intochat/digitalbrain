using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Journaling;
using Orleans.TestingHost;
using Xunit;

namespace Ino.Testing;

/// <summary>
/// Boots two silos sharing one Orleans cluster id. Used by L3 cross-silo
/// integration tests to exercise GrainFactory.GetGrain routing across silo boundaries.
/// </summary>
public sealed class InoMultiSiloFixture : IAsyncLifetime
{
    public TestCluster Cluster { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = new TestClusterBuilder
        {
            Options =
            {
                InitialSilosCount = 2,
                ClusterId = $"ino-l3-{Guid.NewGuid():N}",
                ServiceId = "ino-l3",
            },
        };
        builder.AddSiloBuilderConfigurator<InoMultiSiloSiloConfigurator>();
        Cluster = builder.Build();
        await Cluster.DeployAsync();
    }

    public async ValueTask DisposeAsync()
    {
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

internal sealed class InoMultiSiloSiloConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder silo)
    {
        silo.Services.AddSingleton<IStateMachineStorageProvider, VolatileStateMachineStorageProvider>();
        silo.AddStateMachineStorage();
    }
}
