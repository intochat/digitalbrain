using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Journaling;
using Orleans.TestingHost;

namespace DigitalBrain.Testing;

public sealed class BrainSimulationOptions
{
    public required ModuleAssemblies Modules { get; init; }

    public string Owner { get; init; } = DigitalBrainNames.DefaultOwner;

    public int SiloCount { get; init; } = 1;

    public Action<ISiloBuilder>? ConfigureSilo { get; init; }

    // Host configuration values visible to module Configure hooks (ISiloBuilder.Configuration),
    // e.g. DigitalBrain:Mode=Testing plus the mock-LLM corpus path.
    public IReadOnlyDictionary<string, string?>? Configuration { get; init; }
}

// An in-process Orleans cluster running the production silo composition
// (DigitalBrainRuntime.Add) with only the persistence seams swapped for
// in-memory equivalents: journal storage, grain storage, reminders.
public sealed class BrainSimulation : IAsyncDisposable
{
    private readonly InProcessTestCluster _cluster;

    private BrainSimulation(InProcessTestCluster cluster, string owner)
    {
        _cluster = cluster;
        Grains = cluster.Client;
        Brain = DigitalBrainClient.Connect(cluster.Client, owner);
    }

    public IGrainFactory Grains { get; }

    public IDigitalBrain Brain { get; }

    public static async Task<BrainSimulation> StartAsync(BrainSimulationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var builder = new InProcessTestClusterBuilder((short)options.SiloCount);
        if (options.Configuration is { Count: > 0 } configuration)
        {
            builder.ConfigureHost(host => host.Configuration.AddInMemoryCollection(configuration));
        }

        builder.ConfigureSilo((_, silo) =>
        {
            silo.Services.AddSingleton<IJournalStorageProvider, VolatileJournalStorageProvider>();
            DigitalBrainRuntime.Add(silo, options.Modules);
            silo.AddMemoryGrainStorage(DigitalBrainNames.DefaultGrainStorage);
            silo.UseInMemoryReminderService();
            options.ConfigureSilo?.Invoke(silo);
        });

        // Mirrors DigitalBrainClientHostingExtensions.AddDigitalBrainClient's production client
        // wiring: the in-process cluster client validates its serializer manifest against every
        // [GenerateSerializer] type reachable from loaded assemblies (not only the silo's
        // ModuleAssemblies), so any module whose contracts touch Microsoft.Extensions.AI types
        // (e.g. UI's ChatResponseUpdate/ChatMessage) needs this JSON codec registered
        // client-side too, or ClusterClient construction throws CodecNotFoundException.
        builder.ConfigureClient(client =>
            ModelPayloadSerialization.AddModelPayloadSerialization(client.Services));

        var cluster = builder.Build();
        await cluster.DeployAsync().ConfigureAwait(false);
        return new BrainSimulation(cluster, options.Owner);
    }

    public IDigitalBrain BrainFor(string owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        return DigitalBrainClient.Connect(Grains, owner);
    }

    public string UniqueId(string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        var shortHex = Guid.NewGuid().ToString("N")[..8];
        return $"{prefix}-{shortHex}";
    }

    public async ValueTask DisposeAsync() => await _cluster.DisposeAsync().ConfigureAwait(false);
}
