using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using DigitalBrain.Runtime.Visualization;

namespace DigitalBrain.Kernel.Visualization;

// Per-clientId Channel<VisualLoadHint> fan-out. Single producer
// (FlutterPerfNeuron.Tick), one consumer per active gRPC subscriber.
// _latest holds the most recent hint per client so a fresh subscriber
// receives the current tier immediately on connect.
internal sealed class FlutterPerfHintBroadcaster : IFlutterPerfHintBroadcaster
{
    readonly ConcurrentDictionary<string, List<Channel<VisualLoadHint>>> _subscribers = new();
    readonly ConcurrentDictionary<string, VisualLoadHint> _latest = new();

    public Task BroadcastAsync(VisualLoadHint hint, CancellationToken cancellationToken = default)
    {
        _latest[hint.ClientId] = hint;
        if (!_subscribers.TryGetValue(hint.ClientId, out var channels))
            return Task.CompletedTask;

        List<Channel<VisualLoadHint>> snapshot;
        lock (channels) snapshot = channels.ToList();

        foreach (var ch in snapshot)
            ch.Writer.TryWrite(hint);

        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<VisualLoadHint> SubscribeAsync(
        string clientId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<VisualLoadHint>(new UnboundedChannelOptions
        {
            SingleReader = true, SingleWriter = false,
        });

        var list = _subscribers.GetOrAdd(clientId, _ => new List<Channel<VisualLoadHint>>());
        lock (list) list.Add(channel);

        if (_latest.TryGetValue(clientId, out var seed))
            channel.Writer.TryWrite(seed);

        try
        {
            await foreach (var hint in channel.Reader.ReadAllAsync(cancellationToken))
                yield return hint;
        }
        finally
        {
            lock (list) list.Remove(channel);
            channel.Writer.TryComplete();
        }
    }
}
