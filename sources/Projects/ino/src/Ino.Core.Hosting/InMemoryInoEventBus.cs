using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Ino.Core.Hosting;

/// <summary>
/// In-process <see cref="IInoEventBus"/>. Each <see cref="SubscribeAsync"/> call creates an
/// unbounded channel registered against the user. <see cref="Publish"/> fans out by
/// <c>TryWrite</c>-ing to every channel registered for that user — unbounded channels mean
/// writes always succeed, so a slow subscriber accumulates memory but never blocks a
/// publisher. Acceptable for the POC demo size; a bounded + drop-oldest channel is the
/// post-v0.1 upgrade if subscriber counts grow.
///
/// Cleanup: cancelling the subscription token removes the channel from the registry and
/// completes the writer so the consumer loop exits cleanly.
/// </summary>
public sealed class InMemoryInoEventBus : IInoEventBus
{
    readonly ConcurrentDictionary<string, SubscriberSet> _subscribers = new();

    public void Publish(string userId, InoEvent evt)
    {
        if (!_subscribers.TryGetValue(userId, out var set)) return;
        set.Publish(evt);
    }

    public IAsyncEnumerable<InoEvent> SubscribeAsync(string userId, CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<InoEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        var set = _subscribers.GetOrAdd(userId, _ => new SubscriberSet());
        set.Add(channel);

        ct.Register(() =>
        {
            set.Remove(channel);
            channel.Writer.TryComplete();
            if (set.IsEmpty) _subscribers.TryRemove(userId, out _);
        });

        return channel.Reader.ReadAllAsync(ct);
    }

    sealed class SubscriberSet
    {
        readonly object _lock = new();
        List<Channel<InoEvent>> _channels = new();

        public bool IsEmpty
        {
            get { lock (_lock) return _channels.Count == 0; }
        }

        public void Add(Channel<InoEvent> channel)
        {
            lock (_lock)
            {
                var next = new List<Channel<InoEvent>>(_channels.Count + 1);
                next.AddRange(_channels);
                next.Add(channel);
                _channels = next;
            }
        }

        public void Remove(Channel<InoEvent> channel)
        {
            lock (_lock)
            {
                if (!_channels.Contains(channel)) return;
                var next = new List<Channel<InoEvent>>(_channels);
                next.Remove(channel);
                _channels = next;
            }
        }

        public void Publish(InoEvent evt)
        {
            // Snapshot the channel list under the lock, then write outside. Readers of
            // _channels rely on copy-on-write semantics from Add/Remove above, so we can
            // read-after-release safely.
            List<Channel<InoEvent>> snapshot;
            lock (_lock) snapshot = _channels;
            foreach (var ch in snapshot)
            {
                ch.Writer.TryWrite(evt);
            }
        }
    }
}
