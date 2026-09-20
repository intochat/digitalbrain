using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Microsoft.Extensions.Logging;
using Orleans.Hosting;
using Orleans.TestingHost;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Testing.Unit;

public static class DigitalBrainSimulation
{
    public static async Task<UnitBrain> StartAsync(UnitOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new();
        var builder = new InProcessTestClusterBuilder(1);
        builder.Options.ConfigureFileLogging = false;
        builder.ConfigureHost(host =>
        {
            host.Logging.SetMinimumLevel(LogLevel.Warning);
            foreach (var definition in ModuleComposition.Resolve(options.Modules.OfType<ModuleDefinition>().ToArray()))
            {
                host.Configuration.AddInMemoryCollection(definition.Configuration);
            }
        });
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
            return new UnitBrain(cluster);
        }
        catch
        {
            await UnitBrain.ReleaseAsync(cluster).ConfigureAwait(false);
            throw;
        }
    }
}
