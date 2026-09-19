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
        var settings = options.Value;
        var subscription = new SignalSubscription<T>(settings.BufferCapacity, cancellationToken);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _subscriptions.Add(subscription);
            subscription.Start(async token =>
            {
                var catcher = new Catcher<T>(subscription);
                INeuronObserver? reference = null;
                Task<Guid>? registering = null;
                try
                {
                    reference = client.CreateObjectReference<INeuronObserver>(catcher);
                    registering = source.Watch(reference);
                    var activation = await registering.WaitAsync(settings.OperationTimeout, token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    subscription.Registered();
                    logger.LogInformation("SubscriptionReady {Source} {SignalType}", source.GetGrainId(), typeof(T).Name);
                    while (true)
                    {
                        await Task.Delay(settings.RenewEvery, token).ConfigureAwait(false);
                        var current = await source.Watch(reference).WaitAsync(settings.OperationTimeout, token).ConfigureAwait(false);
                        if (current != activation) { throw new InvalidOperationException("Neuron reactivated; subscribe again. Live signals may have been missed."); }
                    }
                }
                finally
                {
                    if (reference is not null)
                    {
                        if (registering is not null)
                        {
                            _ = registering.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                        }
                        try { await source.Unwatch(reference).WaitAsync(settings.OperationTimeout).ConfigureAwait(false); }
                        catch (Exception error) { logger.LogWarning(error, "Subscription cleanup failed; remote membership expires with its lease."); }
                        finally { client.DeleteObjectReference<INeuronObserver>(reference); }
                    }
                    GC.KeepAlive(catcher);
                    lock (_gate) { _subscriptions.Remove(subscription); }
                }
            });
        }
        try { await subscription.Ready.ConfigureAwait(false); return subscription; }
        catch { await subscription.DisposeAsync().ConfigureAwait(false); throw; }
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
        await Task.WhenAll(subscriptions.Select(s => s.DisposeAsync().AsTask())).ConfigureAwait(false);
    }
    private sealed class Catcher<T>(SignalSubscription<T> subscription) : INeuronObserver where T : Signal
    {
        public Task Hear(Signal signal) { subscription.Hear(signal); return Task.CompletedTask; }
    }
}

