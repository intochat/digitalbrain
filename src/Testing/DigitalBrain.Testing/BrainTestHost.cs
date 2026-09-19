using DigitalBrain.Core;
using DigitalBrain.Contracts;
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
    private readonly List<BehaviorRun> _behaviors = [];
    private readonly List<IAsyncDisposable> _probes = [];
    private BrainTestHost(InProcessTestCluster cluster)
    {
        _cluster = cluster;
        _brain = cluster.Client.ServiceProvider.GetRequiredService<BrainClient>();
    }
    public IDigitalBrain Brain => _brain;
    public IGrainFactory Grains => _cluster.Client;
    public BehaviorRun RunBehavior(Func<IDigitalBrain, CancellationToken, Task> body, CancellationToken ct = default)
    {
        var run = new BehaviorRun(Brain, body, ct);
        _behaviors.Add(run);
        return run;
    }
    public async Task<SignalProbe<T>> ObserveAsync<T>(INeuron source, CancellationToken ct = default) where T : Signal
    {
        var probe = new SignalProbe<T>(await Brain.SubscribeAsync<T>(source, ct).ConfigureAwait(false));
        _probes.Add(probe);
        return probe;
    }
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
        List<Exception> failures = [];
        foreach (var resource in _behaviors.Cast<IAsyncDisposable>().Concat(_probes).Append(_brain))
        {
            try { await resource.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) { failures.Add(error); }
        }
        try { await _cluster.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) { failures.Add(error); }
        if (failures.Count > 0) { throw new AggregateException("Brain test cleanup failed.", failures); }
    }
}
