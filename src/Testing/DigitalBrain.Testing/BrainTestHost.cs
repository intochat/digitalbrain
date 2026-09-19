using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Hosting;
using Orleans.TestingHost;
namespace DigitalBrain.Testing;
public sealed class BrainTestHost : IAsyncDisposable
{
    private readonly InProcessTestCluster _cluster;
    private readonly BrainClient _brain;
    private BrainTestHost(InProcessTestCluster cluster)
    {
        _cluster = cluster;
        _brain = cluster.Client.ServiceProvider.GetRequiredService<BrainClient>();
    }
    public IDigitalBrain Brain => _brain;
    public IGrainFactory Grains => _cluster.Client;
    public static async Task<BrainTestHost> StartAsync(BrainTestOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new();
        var builder = new InProcessTestClusterBuilder(1);
        builder.ConfigureHost(host => host.Logging.SetMinimumLevel(LogLevel.Warning));
        builder.ConfigureSilo((_, silo) =>
        {
            silo.AddDigitalBrain();
            silo.AddMemoryGrainStorage("Default");
            options.ConfigureSilo?.Invoke(silo);
        });
        builder.ConfigureClient(client => { client.AddDigitalBrain(); options.ConfigureClient?.Invoke(client); });
        var cluster = builder.Build();
        try
        {
            await cluster.DeployAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            return new(cluster);
        }
        catch { await cluster.DisposeAsync().ConfigureAwait(false); throw; }
    }
    public async ValueTask DisposeAsync()
    {
        try { await _brain.DisposeAsync().ConfigureAwait(false); }
        finally { await _cluster.DisposeAsync().ConfigureAwait(false); }
    }
}
