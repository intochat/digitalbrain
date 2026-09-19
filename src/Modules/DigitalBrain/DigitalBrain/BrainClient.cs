using DigitalBrain.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Core;

public sealed class BrainClient(IClusterClient client, IOptions<BrainOptions> options, ILogger<BrainClient> logger)
    : IDigitalBrain, IAsyncDisposable
{
    private readonly Lock _gate = new();
    private readonly HashSet<IAsyncDisposable> _subscriptions = [];
    private bool _disposed;

    public T Get<T>(string id) where T : class, IGrainWithStringKey => client.GetGrain<T>(id);

    public async Task<SignalSubscription<T>> SubscribeAsync<T>(INeuron source, CancellationToken cancellationToken = default) where T : Signal
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        SignalSubscription<T> subscription;
        Task connecting;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            subscription = new(client, source, options.Value, logger, Remove, cancellationToken);
            _subscriptions.Add(subscription); // Orleans holds observer targets weakly; this owns the strong reference.
            connecting = subscription.ConnectAsync();
        }
        await connecting.ConfigureAwait(false);
        return subscription;
    }

    private void Remove(IAsyncDisposable subscription)
    {
        lock (_gate) { _subscriptions.Remove(subscription); }
    }

    public async ValueTask DisposeAsync()
    {
        IAsyncDisposable[] subscriptions;
        lock (_gate)
        {
            if (_disposed) { return; }
            _disposed = true;
            subscriptions = [.. _subscriptions];
        }
        await Task.WhenAll(subscriptions.Select(subscription => subscription.DisposeAsync().AsTask())).ConfigureAwait(false);
    }
}
