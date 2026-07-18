using System.Collections.Concurrent;
using System.Threading.Channels;
using Ino.Core.Brain;

namespace Ino.Core.Hosting.Brain;

// In-process fan-out hub for BrainPulse.
// BrainTraceFilter publishes via IBrainPulseSink; WatchBrainActivity subscribes here.
// Both run in the same kernel silo process — no Orleans stream required. This avoids
// the embedded-client stream-subscription problem (AddMemoryStreams on ISiloBuilder
// does not automatically register the "ino-brain" provider in the embedded IClusterClient;
// the client-side stream subscription silently yields no deliveries).
public sealed class BrainPulseHub : IBrainPulseSink
{
    readonly ConcurrentDictionary<Guid, Channel<BrainPulse>> _subscribers = new();

    // IBrainPulseSink: called by BrainTraceFilter after every grain call.
    // Synchronous fan-out — never blocks the grain call path.
    public Task EmitAsync(BrainPulse pulse, CancellationToken ct)
    {
        foreach (var ch in _subscribers.Values)
            ch.Writer.TryWrite(pulse);
        return Task.CompletedTask;
    }

    // Returns a channel reader that receives every pulse until ct is cancelled.
    public ChannelReader<BrainPulse> Subscribe(CancellationToken ct)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<BrainPulse>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        _subscribers[id] = channel;

        ct.Register(() =>
        {
            _subscribers.TryRemove(id, out _);
            channel.Writer.TryComplete();
        });

        return channel.Reader;
    }
}
