using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Orleans.Streams;

namespace DigitalBrain.Kernel.Ui;

using DigitalBrain.Ui.Contracts;
using DigitalBrain.Ui.Contracts.Ui;

// Server-driven-UI backbone. BroadcastAsync publishes an RfwCard onto an Orleans stream keyed by the card's
// ClientId (or a well-known unaddressed key when ClientId is null); Orleans's own pub-sub delivers it to
// exactly the WatchHomeFeed connections subscribed to that key, cluster-wide, regardless of which silo
// published it or which silo the subscriber is attached to. Each WatchHomeFeed call subscribes directly to
// its own client stream plus the shared unaddressed stream via SubscribeAsync — there is no in-process
// subscriber registry and no per-silo relay; Orleans tracks who is listening.
public sealed class HomeFeedBus(IClusterClient clusterClient, ILogger<HomeFeedBus>? logger = null)
{
    private const int MaxSeenEntries = 5_000;
    private const string ProviderName = "HomeFeed";
    private const string StreamNamespace = "homefeed";
    private static readonly Guid UnaddressedKey = Guid.Empty;
    private readonly HashSet<string> _seen = new();
    private readonly Queue<string> _seenOrder = new();
    private readonly object _seenLock = new();

    public async Task BroadcastAsync(RfwCard card)
    {
        if (IsDuplicate(card)) return;

        await PublishAsync(card);
    }

    // Compatibility path for legacy synchronous callers. Prefer BroadcastAsync from async handlers/tests so
    // publish failures are observed by the caller instead of only being logged.
    public void Broadcast(RfwCard card)
    {
        _ = BroadcastAndLogAsync(card);
    }

    private async Task BroadcastAndLogAsync(RfwCard card)
    {
        if (IsDuplicate(card)) return;

        try
        {
            await PublishAsync(card);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "HomeFeed stream publish failed for clientId={ClientId}", card.ClientId);
        }
    }

    // One subscription per WatchHomeFeed gRPC call: the caller's own personal stream (only if it supplied a
    // clientId) plus the shared unaddressed stream every connection receives. DisposeAsync unsubscribes both.
    public async Task<Subscription> SubscribeAsync(string? clientId)
    {
        var channel = Channel.CreateUnbounded<RfwCard>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        var provider = clusterClient.GetStreamProvider(ProviderName);

        Task OnCard(RfwCard card, StreamSequenceToken _)
        {
            channel.Writer.TryWrite(card);
            return Task.CompletedTask;
        }

        var unaddressedHandle = await provider.GetStream<RfwCard>(StreamId.Create(StreamNamespace, UnaddressedKey)).SubscribeAsync(OnCard);

        StreamSubscriptionHandle<RfwCard>? personalHandle = null;
        if (!string.IsNullOrWhiteSpace(clientId))
        {
            personalHandle = await provider.GetStream<RfwCard>(StreamId.Create(StreamNamespace, clientId)).SubscribeAsync(OnCard);
        }

        return new Subscription(channel, unaddressedHandle, personalHandle);
    }

    private static StreamId StreamIdFor(string? clientId) =>
        clientId is null
            ? StreamId.Create(StreamNamespace, UnaddressedKey)
            : StreamId.Create(StreamNamespace, clientId);

    private Task PublishAsync(RfwCard card) =>
        clusterClient
            .GetStreamProvider(ProviderName)
            .GetStream<RfwCard>(StreamIdFor(card.ClientId))
            .OnNextAsync(card);

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
