using DigitalBrain.Contracts;
using Microsoft.Extensions.Options;
namespace DigitalBrain.Core;
public sealed class BrainClient(IClusterClient client, IOptions<BrainOptions> options) : IDigitalBrain, IAsyncDisposable
{
    private readonly List<IAsyncDisposable> _subscriptions = [];
    public T Get<T>(string id) where T : class, IGrainWithStringKey => client.GetGrain<T>(id);
    public async Task<SignalSubscription<T>> SubscribeAsync<T>(INeuron source, CancellationToken cancellationToken = default) where T : Signal
    {
        cancellationToken.ThrowIfCancellationRequested();
        INeuronObserver? reference = null;
        var subscription = new SignalSubscription<T>(options.Value.BufferCapacity, async () =>
        {
            if (reference is null) { return; }
            try { await source.Unwatch(reference).ConfigureAwait(false); }
            finally { client.DeleteObjectReference<INeuronObserver>(reference); }
        });
        reference = client.CreateObjectReference<INeuronObserver>(new Catcher<T>(subscription));
        await source.Watch(reference).ConfigureAwait(false);
        _subscriptions.Add(subscription);
        return subscription;
    }
    public async ValueTask DisposeAsync()
    {
        foreach (var subscription in _subscriptions) { await subscription.DisposeAsync().ConfigureAwait(false); }
        _subscriptions.Clear();
    }
    private sealed class Catcher<T>(SignalSubscription<T> subscription) : INeuronObserver where T : Signal
    {
        public Task Hear(Signal signal) { subscription.Hear(signal); return Task.CompletedTask; }
    }
}
