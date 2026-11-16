using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace DigitalBrain.Kernel;

/// <summary>
/// Synchronous-registration channel fan-out for <see cref="SignalDelivery"/> streams: registers the listener channel before
/// returning <see cref="IAsyncEnumerable{SignalDelivery}"/> so callers never miss deliveries emitted immediately after subscribing.
/// </summary>
internal sealed class DeliverySubscriptions
{
    private readonly ConcurrentDictionary<int, (string? Brain, Channel<SignalDelivery> Channel)> _listeners = new();
    private int _next;

    public void Publish(string? brain, SignalDelivery delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        foreach (var listener in _listeners.Values)
        {
            if (brain is null || string.Equals(listener.Brain, brain, StringComparison.Ordinal))
            {
                listener.Channel.Writer.TryWrite(delivery);
            }
        }
    }

    public IAsyncEnumerable<SignalDelivery> Listen(string? brain, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Canceled(cancellationToken);
        }

        // Register synchronously so deliveries emitted immediately after Listen() returns are never missed.
        return new DeliveryStream(this, brain, Subscribe(brain), cancellationToken);
    }

    private Subscription Subscribe(string? brain)
    {
        var id = Interlocked.Increment(ref _next);
        var channel = Channel.CreateUnbounded<SignalDelivery>();
        _listeners[id] = (brain, channel);
        return new Subscription(id, channel);
    }

    private void Unsubscribe(int id) => _listeners.TryRemove(id, out _);

    private static async IAsyncEnumerable<SignalDelivery> Canceled([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }

    private sealed record Subscription(int Id, Channel<SignalDelivery> Channel);

    private sealed class DeliveryStream : IAsyncEnumerable<SignalDelivery>
    {
        private readonly DeliverySubscriptions _owner;
        private readonly string? _brain;
        private readonly CancellationToken _subscriptionToken;
        private readonly CancellationTokenRegistration _cancelUnused;
        private Subscription? _initial;

        public DeliveryStream(DeliverySubscriptions owner, string? brain, Subscription initial, CancellationToken subscriptionToken)
        {
            _owner = owner;
            _brain = brain;
            _subscriptionToken = subscriptionToken;
            _initial = initial;
            if (subscriptionToken.CanBeCanceled)
            {
                _cancelUnused = subscriptionToken.Register(static state => ((DeliveryStream)state!).ReleaseUnused(), this);
            }
        }

        public IAsyncEnumerator<SignalDelivery> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            _cancelUnused.Dispose();
            var initial = Interlocked.Exchange(ref _initial, null);
            var linked = _subscriptionToken.CanBeCanceled && cancellationToken.CanBeCanceled && _subscriptionToken != cancellationToken
                ? CancellationTokenSource.CreateLinkedTokenSource(_subscriptionToken, cancellationToken)
                : null;
            var token = linked?.Token ?? (_subscriptionToken.CanBeCanceled ? _subscriptionToken : cancellationToken);
            if (token.IsCancellationRequested)
            {
                if (initial is not null) { _owner.Unsubscribe(initial.Id); }
                linked?.Dispose();
                return Canceled(token).GetAsyncEnumerator(CancellationToken.None);
            }

            var subscription = initial ?? _owner.Subscribe(_brain);
            var reader = subscription.Channel.Reader.ReadAllAsync(token).GetAsyncEnumerator(token);
            return new Enumerator(_owner, subscription.Id, reader, linked);
        }

        private void ReleaseUnused()
        {
            if (Interlocked.Exchange(ref _initial, null) is { } unused)
            {
                _owner.Unsubscribe(unused.Id);
            }
        }

        private sealed class Enumerator(DeliverySubscriptions owner, int id, IAsyncEnumerator<SignalDelivery> inner, CancellationTokenSource? linked) : IAsyncEnumerator<SignalDelivery>
        {
            public SignalDelivery Current => inner.Current;

            public ValueTask<bool> MoveNextAsync() => inner.MoveNextAsync();

            public async ValueTask DisposeAsync()
            {
                try { await inner.DisposeAsync().ConfigureAwait(false); }
                finally
                {
                    owner.Unsubscribe(id);
                    linked?.Dispose();
                }
            }
        }
    }
}
