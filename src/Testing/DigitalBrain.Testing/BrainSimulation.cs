using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Journaling;
using Orleans.Storage;
using Orleans.TestingHost;

namespace DigitalBrain.Testing;

public sealed class BrainSimulationOptions
{
    public required ModuleManifest Modules { get; init; }
    public Action<ISiloBuilder>? ConfigureSilo { get; init; }
    public string? PersistenceDirectory { get; init; }
    public IReadOnlyDictionary<string, string?>? Configuration { get; init; }
}

public sealed class BrainSimulation : IAsyncDisposable
{
    private readonly InProcessTestCluster inProcess;

    private BrainSimulation(InProcessTestCluster cluster)
    {
        inProcess = cluster;
        Grains = cluster.Client;
    }

    public IGrainFactory Grains { get; }

    public static async Task<BrainSimulation> StartAsync(BrainSimulationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var builder = new InProcessTestClusterBuilder(1);
        builder.ConfigureHost(static host => host.Logging.SetMinimumLevel(LogLevel.Warning));
        if (options.Configuration is { Count: > 0 } configuration)
        {
            builder.ConfigureHost(host => host.Configuration.AddInMemoryCollection(configuration));
        }
        builder.ConfigureSilo((_, silo) => ConfigureSilo(silo, options));
        builder.ConfigureClient(client => ModelPayloadSerialization.AddModelPayloadSerialization(client.Services));
        var cluster = builder.Build();
        await cluster.DeployAsync().ConfigureAwait(false);
        return new(cluster);
    }

    private static void ConfigureSilo(ISiloBuilder silo, BrainSimulationOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.PersistenceDirectory))
        {
            silo.Services.AddSingleton<IJournalStorageProvider>(new FileJournalStorageProvider(options.PersistenceDirectory));
            silo.AddReminders();
            silo.Services.AddSingleton<IReminderTable>(new FileReminderTable(options.PersistenceDirectory));
            silo.Services.Configure<ReminderOptions>(reminders => reminders.MinimumReminderPeriod = TimeSpan.FromSeconds(1));
            // Seconds prove a cold restart within a test's patience; Orleans skips ticks missed while the cluster is down.
            silo.Services.AddSingleton(new NeuronOptions { RetryReminderPeriod = TimeSpan.FromSeconds(2) });
        }
        else
        {
            silo.Services.AddSingleton<IJournalStorageProvider, VolatileJournalStorageProvider>();
            silo.UseInMemoryReminderService();
        }
        DigitalBrainRuntime.Add(silo, options.Modules);
        if (!string.IsNullOrWhiteSpace(options.PersistenceDirectory))
        {
            silo.Services.AddKeyedSingleton<IGrainStorage>(DigitalBrainNames.DefaultGrainStorage,
                (services, _) => new FileGrainStorage(options.PersistenceDirectory,
                    services.GetRequiredService<Orleans.Serialization.Serializer>()));
        }
        else
        {
            silo.AddMemoryGrainStorage(DigitalBrainNames.DefaultGrainStorage);
        }
        options.ConfigureSilo?.Invoke(silo);
    }

    public async Task RestartSiloAsync(CancellationToken cancellationToken = default)
    {
        var silo = inProcess.GetActiveSilos().Single();
        await inProcess.RestartSiloAsync(silo).WaitAsync(cancellationToken).ConfigureAwait(false);
        await inProcess.WaitForLivenessToStabilizeAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => inProcess.DisposeAsync();
}
