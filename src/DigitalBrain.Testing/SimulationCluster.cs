using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Journaling;
using Orleans.TestingHost;

namespace DigitalBrain.Testing;

public static class SimulationCluster
{
    private static InProcessTestCluster? _cluster;
    private static SynapseObserver? _observer;

    public static IGrainFactory Grains => Deployed().Client;

    public static SynapseObserver Observed => _observer
        ?? throw new InvalidOperationException($"The simulation cluster is not running. Call {nameof(SimulationCluster)}.{nameof(StartAsync)} before a scenario runs.");

    public static async Task StartAsync()
    {
        if (_cluster is not null)
        {
            return;
        }

        var journalStorage = new VolatileJournalStorageProvider();
        var builder = new InProcessTestClusterBuilder();

        builder.ConfigureSilo((_, silo) =>
        {
            silo.AddDigitalBrain();
            silo.UseInMemoryReminderService();
            silo.Services.AddSingleton<IJournalStorageProvider>(journalStorage);
        });

        var cluster = builder.Build();
        await cluster.DeployAsync();

        _observer = new SynapseObserver();
        _cluster = cluster;
    }

    public static async Task RestartAsync()
    {
        var cluster = Deployed();

        foreach (var silo in cluster.Silos.ToList())
        {
            await cluster.RestartSiloAsync(silo);
        }
    }

    public static async Task StopAsync()
    {
        if (_cluster is null)
        {
            return;
        }

        _observer?.Dispose();
        _observer = null;

        await _cluster.StopAllSilosAsync();
        await _cluster.DisposeAsync();
        _cluster = null;
    }

    private static InProcessTestCluster Deployed() => _cluster
        ?? throw new InvalidOperationException($"The simulation cluster is not running. Call {nameof(SimulationCluster)}.{nameof(StartAsync)} before a scenario runs.");
}
