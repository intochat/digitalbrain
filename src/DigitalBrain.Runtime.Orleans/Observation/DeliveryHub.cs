using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using DigitalBrain.Spi;

namespace DigitalBrain.Runtime.Orleans;

/// <summary>In-memory fan-out of synapse deliveries on this silo. A listener hears what is delivered after it subscribes.</summary>
internal sealed class DeliveryHub
{
    private readonly ConcurrentDictionary<int, (string Brain, Channel<SignalDelivery> Channel)> _listeners = new();
    private int _next;

    /// <summary>Hands <paramref name="delivery"/> to the listeners of <paramref name="brain"/>.</summary>
    public void Publish(string brain, SignalDelivery delivery)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(brain);
        ArgumentNullException.ThrowIfNull(delivery);
        foreach (var listener in _listeners.Values)
        {
            if (string.Equals(listener.Brain, brain, StringComparison.Ordinal)) { listener.Channel.Writer.TryWrite(delivery); }
        }
    }

    /// <summary>Deliveries of <paramref name="brain"/> from now on.</summary>
    public async IAsyncEnumerable<SignalDelivery> Listen(string brain, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(brain);
        var id = Interlocked.Increment(ref _next);
        var channel = Channel.CreateUnbounded<SignalDelivery>();
        _listeners[id] = (brain, channel);
        try
        {
            await foreach (var delivery in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return delivery;
            }
        }
        finally
        {
            _listeners.TryRemove(id, out _);
        }
    }
}
