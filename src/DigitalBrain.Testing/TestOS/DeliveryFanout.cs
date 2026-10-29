using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using DigitalBrain.Spi;

namespace DigitalBrain.Testing;

internal sealed class DeliveryFanout
{
    private readonly ConcurrentDictionary<int, Channel<SignalDelivery>> _listeners = new();
    private int _next;

    public void Publish(SignalDelivery delivery)
    {
        foreach (var channel in _listeners.Values) { channel.Writer.TryWrite(delivery); }
    }

    public async IAsyncEnumerable<SignalDelivery> Listen([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _next);
        var channel = Channel.CreateUnbounded<SignalDelivery>();
        _listeners[id] = channel;
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
