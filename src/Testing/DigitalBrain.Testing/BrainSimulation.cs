using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orleans.TestingHost;

namespace DigitalBrain.Testing;

public sealed class BrainSimulationOptions
{
    public Action<ISiloBuilder>? ConfigureSilo { get; init; }
    public IReadOnlyDictionary<string, string?>? Configuration { get; init; }
}

public sealed class BrainSimulation : IDigitalBrain
{
    private readonly InProcessTestCluster _inProcess;

    private BrainSimulation(InProcessTestCluster cluster) => _inProcess = cluster;

    public IServiceProvider SiloServices => _inProcess.GetActiveSilos().Single().ServiceProvider;

    public T Get<T>(string id) where T : class, IGrainWithStringKey
        => _inProcess.Client.GetGrain<T>(id);

    public static Task<IDigitalBrain> StartAsync() => StartAsync(new());

    public static async Task<IDigitalBrain> StartAsync(BrainSimulationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var builder = new InProcessTestClusterBuilder(1);
        builder.ConfigureHost(static host => host.Logging.SetMinimumLevel(LogLevel.Warning));
        if (options.Configuration is { Count: > 0 } configuration)
        {
            builder.ConfigureHost(host => host.Configuration.AddInMemoryCollection(configuration));
        }

        builder.ConfigureSilo((_, silo) =>
        {
            silo.AddNeuronBroadcast();
            options.ConfigureSilo?.Invoke(silo);
        });
        builder.ConfigureClient(static client => client.AddNeuronBroadcast());
        var cluster = builder.Build();
        await cluster.DeployAsync().ConfigureAwait(false);
        return new BrainSimulation(cluster);
    }

    public async Task RestartSiloAsync(CancellationToken cancellationToken = default)
    {
        var silo = _inProcess.GetActiveSilos().Single();
        await _inProcess.RestartSiloAsync(silo).WaitAsync(cancellationToken).ConfigureAwait(false);
        await _inProcess.WaitForLivenessToStabilizeAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => _inProcess.DisposeAsync();
}
