using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using DigitalBrain.Core.Ui;
using Orleans.Streams;

namespace DigitalBrain.Kernel.Ui;

// Server-driven-UI backbone. Broadcast publishes an RfwCard onto an Orleans stream keyed by the card's
// ClientId (or a well-known unaddressed key when ClientId is null); Orleans's own pub-sub delivers it to
// exactly the WatchHomeFeed connections subscribed to that key, cluster-wide, regardless of which silo
// published it or which silo the subscriber is attached to. Each WatchHomeFeed call subscribes directly to
// its own client stream plus the shared unaddressed stream via SubscribeAsync — there is no in-process
// subscriber registry and no per-silo relay; Orleans tracks who is listening.
public sealed class HomeFeedBus(IClusterClient clusterClient, ILogger<HomeFeedBus>? logger = null)
{
    private const int MaxSeenEntries = 5_000;
    private static readonly Guid UnaddressedKey = Guid.Empty;
    private readonly HashSet<string> _seen = new();
    private readonly Queue<string> _seenOrder = new();
    private readonly object _seenLock = new();

    public void Broadcast(RfwCard card)
    {
        if (IsDuplicate(card)) return;

        var streamId = card.ClientId is null
            ? StreamId.Create("homefeed", UnaddressedKey)
            : StreamId.Create("homefeed", card.ClientId);

        _ = Task.Run(async () =>
        {
            try
            {
                await clusterClient.GetStreamProvider("HomeFeed").GetStream<RfwCard>(streamId).OnNextAsync(card);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "HomeFeed stream publish failed for clientId={ClientId}", card.ClientId);
            }
        });
    }

    // One subscription per WatchHomeFeed gRPC call: the caller's own personal stream (only if it supplied a
    // clientId) plus the shared unaddressed stream every connection receives. DisposeAsync unsubscribes both.
    public async Task<Subscription> SubscribeAsync(string? clientId)
    {
        var channel = Channel.CreateUnbounded<RfwCard>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        var provider = clusterClient.GetStreamProvider("HomeFeed");

        Task OnCard(RfwCard card, StreamSequenceToken _)
        {
            channel.Writer.TryWrite(card);
            return Task.CompletedTask;
        }

        var unaddressedHandle = await provider.GetStream<RfwCard>(StreamId.Create("homefeed", UnaddressedKey)).SubscribeAsync(OnCard);

        StreamSubscriptionHandle<RfwCard>? personalHandle = null;
        if (!string.IsNullOrWhiteSpace(clientId))
        {
            personalHandle = await provider.GetStream<RfwCard>(StreamId.Create("homefeed", clientId)).SubscribeAsync(OnCard);
        }

        return new Subscription(channel, unaddressedHandle, personalHandle);
    }

    private bool IsDuplicate(RfwCard card)
    {
        var key = $"{card.CorrelationId}|{ContentHash(card)}";
        lock (_seenLock)
        {
            if (!_seen.Add(key)) return true;
            _seenOrder.Enqueue(key);
            while (_seenOrder.Count > MaxSeenEntries)
                _seen.Remove(_seenOrder.Dequeue());
            return false;
        }
    }

    private static string ContentHash(RfwCard card) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{card.LibraryName}|{card.RootWidget}|{card.DataJson}")));

    public sealed class Subscription(
        Channel<RfwCard> channel,
        StreamSubscriptionHandle<RfwCard> unaddressedHandle,
        StreamSubscriptionHandle<RfwCard>? personalHandle) : IAsyncDisposable
    {
        public ChannelReader<RfwCard> Reader { get; } = channel.Reader;

        public async ValueTask DisposeAsync()
        {
            await unaddressedHandle.UnsubscribeAsync();
            if (personalHandle is not null)
                await personalHandle.UnsubscribeAsync();
            channel.Writer.TryComplete();
        }
    }
}
