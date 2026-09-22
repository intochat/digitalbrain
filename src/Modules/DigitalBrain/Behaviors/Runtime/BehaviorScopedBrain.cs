using DigitalBrain.Contracts;
using Orleans;

namespace DigitalBrain.Core;

internal sealed class BehaviorScopedBrain(IDigitalBrain inner, BehaviorReadiness readiness, BehaviorGeneration generation) : IDigitalBrain
{
    private readonly List<IAsyncDisposable> _subscriptions = [];
    private bool _disposed;
    public T Get<T>(string id) where T : class, IGrainWithStringKey => inner.Get<T>(id);
    public async Task<ISignalSubscription<T>> SubscribeAsync<T>(INeuron source, CancellationToken cancellationToken = default) where T : Signal
    {
        var subscription = await inner.SubscribeAsync<T>(source, cancellationToken).ConfigureAwait(false);
        bool rejected;
        lock (_subscriptions) { rejected = _disposed; if (!rejected) { _subscriptions.Add(subscription); } }
        if (rejected)
        {
            await subscription.DisposeAsync().ConfigureAwait(false);
            throw new ObjectDisposedException(nameof(BehaviorScopedBrain));
        }
        var identity = source.GetGrainId().ToString();
        readiness.SubscriptionReady(generation, identity, typeof(T));
        _ = subscription.Completion.ContinueWith(task =>
        {
            _ = task.Exception;
            readiness.SubscriptionClosed(generation, identity, typeof(T));
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return subscription;
    }
    public async ValueTask DisposeAsync()
    {
        readiness.End(generation);
        IAsyncDisposable[] subscriptions;
        lock (_subscriptions) { _disposed = true; subscriptions = [.. _subscriptions]; _subscriptions.Clear(); }
        List<Exception> failures = [];
        foreach (var subscription in subscriptions)
        {
            try { await subscription.DisposeAsync().ConfigureAwait(false); } catch (Exception error) { failures.Add(error); }
        }
        if (failures.Count > 0) { throw new AggregateException(failures); }
    }
}
