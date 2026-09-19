using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Runtime;
using Orleans.TestingHost;

namespace DigitalBrain.Testing;

internal sealed class SimulatedBrain(InProcessTestCluster cluster, WebApplication? web, HttpClient? http,
    FileStream? storeLease, string? temporaryStore, StorageFaults? faults) : IDigitalBrain
{
    private readonly IDigitalBrain _brain = cluster.Client.ServiceProvider.GetRequiredService<IDigitalBrain>();
    private readonly List<IAsyncDisposable> _resources = [];
    private int _disposed;

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
        List<Exception> failures = [];
        faults?.Dispose();
        foreach (var resource in _resources)
        {
            try { await resource.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) { failures.Add(error); }
        }
        http?.Dispose();
        if (web is not null)
        {
            try { await web.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) { failures.Add(error); }
        }
        try { await _brain.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) { failures.Add(error); }
        try { await cluster.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) { failures.Add(error); }
        storeLease?.Dispose();
        if (temporaryStore is not null) { Directory.Delete(temporaryStore, true); }
        if (failures.Count > 0) { throw new AggregateException("Brain simulation cleanup failed.", failures); }
    }
}
