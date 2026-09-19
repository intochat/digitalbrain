using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Microsoft.Extensions.Logging;
using Orleans.Hosting;
using Orleans.TestingHost;

namespace DigitalBrain.Testing;

public static class DigitalBrainSimulation
{
    public static async Task<IDigitalBrain> StartAsync(SimulationOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new();
        var builder = new InProcessTestClusterBuilder(1);
        builder.Options.ConfigureFileLogging = false;
        builder.ConfigureHost(host => host.Logging.SetMinimumLevel(LogLevel.Warning));
        builder.ConfigureSilo((_, silo) =>
        {
            silo.AddDigitalBrain();
            foreach (var module in options.Modules) { module.Configure(silo); }
            silo.AddMemoryGrainStorage("Default");
            if (options.UseReminders) { silo.UseInMemoryReminderService(); }
            options.ConfigureSilo?.Invoke(silo);
        });
        builder.ConfigureClient(client => { client.AddDigitalBrain(); options.ConfigureClient?.Invoke(client); });
        InProcessTestCluster? cluster = null;
        try
        {
            cluster = builder.Build();
            await cluster.DeployAsync(cancellationToken).ConfigureAwait(false);
            return new SimulatedBrain(cluster);
        }
        catch
        {
            await SimulatedBrain.ReleaseAsync(cluster).ConfigureAwait(false);
            throw;
        }
    }
}
