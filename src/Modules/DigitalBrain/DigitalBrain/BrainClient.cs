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
                        registering = source.Watch(reference);
                        var current = await registering.WaitAsync(settings.OperationTimeout, token).ConfigureAwait(false);
                        if (current != activation) { throw new InvalidOperationException("Neuron reactivated; subscribe again. Live signals may have been missed."); }
                    }
                }
                finally
                {
                    if (reference is not null)
                    {
                        if (registering is { IsCompleted: false })
                        {
                            // Disposal must not wait indefinitely for a remote call. Observe its
                            // eventual result and remove membership added after immediate cleanup.
                            _ = CleanupAfterRegistrationAsync(registering, source, reference, settings.OperationTimeout);
                        }
                        else { _ = registering?.Exception; }
                        try { await UnwatchAsync(source, reference, settings.OperationTimeout).ConfigureAwait(false); }
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
    private async Task CleanupAfterRegistrationAsync(Task<Guid> registering, INeuron source, INeuronObserver reference, TimeSpan timeout)
    {
        try { await registering.ConfigureAwait(false); }
        catch { return; }
        await UnwatchAsync(source, reference, timeout).ConfigureAwait(false);
    }
    private async Task UnwatchAsync(INeuron source, INeuronObserver reference, TimeSpan timeout)
    {
        try { await source.Unwatch(reference).WaitAsync(timeout).ConfigureAwait(false); }
        catch (Exception error) { logger.LogWarning(error, "Subscription cleanup failed; remote membership expires with its lease."); }
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

