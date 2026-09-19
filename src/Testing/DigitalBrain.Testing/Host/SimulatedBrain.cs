using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Runtime;
using Orleans.TestingHost;

namespace DigitalBrain.Testing;

internal sealed class SimulatedBrain(InProcessTestCluster cluster, WebApplication? web, HttpClient? http, StorageFaults? faults)
    : IDigitalBrain
{
    private readonly IDigitalBrain _brain = cluster.Client.ServiceProvider.GetRequiredService<IDigitalBrain>();
    private readonly List<IAsyncDisposable> _resources = [];
    private int _disposed;
    internal int BufferCapacity { get; } = cluster.Client.ServiceProvider.GetRequiredService<IOptions<BrainOptions>>().Value.BufferCapacity;

    public IGrainFactory Grains => cluster.Client;

    internal IDigitalBrain Client => _brain;

    internal HttpClient Endpoints => http ?? throw new InvalidOperationException("Start the simulation with UseHttp to reach module endpoints.");

    internal void Track(IAsyncDisposable resource) => _resources.Add(resource);

    public T Get<T>(string id) where T : class, IGrainWithStringKey => _brain.Get<T>(id);

    public Task<ISignalSubscription<T>> SubscribeAsync<T>(INeuron source, CancellationToken cancellationToken = default) where T : Signal
        => _brain.SubscribeAsync<T>(source, cancellationToken);

    public Task DeactivateAsync(INeuron neuron, CancellationToken cancellationToken = default)
        => cluster.DeactivateAsync(neuron.GetGrainId()).WaitAsync(cancellationToken);

    public async Task RestartSiloAsync(CancellationToken cancellationToken = default)
    {
        await cluster.RestartSiloAsync(cluster.GetActiveSilos().Single()).WaitAsync(cancellationToken).ConfigureAwait(false);
        await cluster.WaitForLivenessToStabilizeAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) { return; }
        var failures = await ReleaseAsync(cluster, web, http, faults, _brain, _resources).ConfigureAwait(false);
        if (failures.Count > 0) { throw new AggregateException("Brain simulation cleanup failed.", failures); }
    }

    internal static async Task<List<Exception>> ReleaseAsync(
        InProcessTestCluster? cluster, WebApplication? web, HttpClient? http, StorageFaults? faults,
        IDigitalBrain? brain = null, IReadOnlyList<IAsyncDisposable>? resources = null)
    {
        List<Exception> failures = [];
        faults?.Dispose();
        if (resources is not null)
        {
            foreach (var resource in resources)
            {
                try { await resource.DisposeAsync().ConfigureAwait(false); }
                catch (Exception error) { failures.Add(error); }
            }
        }
        http?.Dispose();
        if (web is not null)
        {
            try { await web.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) { failures.Add(error); }
        }
        if (brain is not null)
        {
            try { await brain.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) { failures.Add(error); }
        }
        if (cluster is not null)
        {
            try { await cluster.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) { failures.Add(error); }
        }
        return failures;
    }
}
