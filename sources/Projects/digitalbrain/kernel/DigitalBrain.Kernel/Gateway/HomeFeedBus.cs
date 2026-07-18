using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using DigitalBrain.Runtime.Ui;
using DigitalBrain.Kernel.Conversation;

namespace DigitalBrain.Kernel.Gateway;

// Singleton fanout for RfwCards that the TimelineRelayGrain forwards from
// the global synapse timeline. Each WatchHomeFeed gRPC call opens its own
// unbounded channel; the relay grain calls BroadcastAsync for every RfwCard.
// The bus also persists each card into ConversationGrain("default") so that
// the assistant message history reflects every RFW widget pushed to Flutter.
public sealed class HomeFeedBus(IGrainFactory grains, ILogger<HomeFeedBus> logger)
{
    // Cap the dedupe window so long-running silos don't accumulate one entry
    // per RFW card forever. 5000 ≈ a day's worth of cards at the demo cadence.
    const int MaxSeenEntries = 5_000;

    readonly ConcurrentDictionary<Guid, Channel<RfwCard>> _subscribers = new();
    readonly HashSet<(Guid correlationId, string contentHash)> _seen = new();
    readonly Queue<(Guid correlationId, string contentHash)> _seenOrder = new();
    readonly SemaphoreSlim _seenLock = new(1, 1);

    public Subscription Subscribe()
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<RfwCard>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
        _subscribers[id] = channel;
        return new Subscription(this, id, channel);
    }

    public async Task BroadcastAsync(RfwCard card, CancellationToken ct = default)
    {
        await PersistToConversationAsync(card, ct);

        foreach (var (_, channel) in _subscribers)
            channel.Writer.TryWrite(card);
    }

    async Task PersistToConversationAsync(RfwCard card, CancellationToken ct)
    {
        var contentHash = ComputeContentHash(card);
        var key = (card.CorrelationId, contentHash);

        await _seenLock.WaitAsync(ct);
        try
        {
            if (!_seen.Add(key)) return;
            _seenOrder.Enqueue(key);
            while (_seenOrder.Count > MaxSeenEntries)
                _seen.Remove(_seenOrder.Dequeue());
        }
        finally { _seenLock.Release(); }

        var rfwJson = JsonSerializer.Serialize(new
        {
            card.LibraryName,
            card.RootWidget,
            card.DataJson,
        });

        try
        {
            var conv = grains.GetGrain<IConversation>("default");
            await conv.AppendAssistantMessageAsync(Guid.NewGuid(), text: null, rfwEnvelopeJson: rfwJson, card.CorrelationId, ct);
        }
        catch (Exception ex)
        {
            // Persistence failure must not block the live feed.
            logger.LogWarning(ex, "Failed to persist RFW card (correlation {CorrelationId}) to ConversationGrain.", card.CorrelationId);
        }
    }

    static string ComputeContentHash(RfwCard card)
    {
        var raw = $"{card.LibraryName}|{card.RootWidget}|{card.DataJson}";
        var bytes = Encoding.UTF8.GetBytes(raw);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    public sealed class Subscription(HomeFeedBus owner, Guid id, Channel<RfwCard> channel) : IDisposable
    {
        public ChannelReader<RfwCard> Reader { get; } = channel.Reader;

        public void Dispose()
        {
            if (owner._subscribers.TryRemove(id, out _))
                channel.Writer.TryComplete();
        }
    }
}
