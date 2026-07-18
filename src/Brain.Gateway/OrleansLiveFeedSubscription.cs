using Brain.Contracts;
using Orleans.Runtime;
using Orleans.Streams;

namespace Brain.Gateway;

public sealed class OrleansLiveFeedSubscription(
    IClusterClient clusterClient,
    string streamProviderName,
    string streamNamespace,
    Guid streamId) : ILiveFeedSubscription
{
    private StreamSubscriptionHandle<EventSynapse<UiSurfacePatch>>? _handle;
    private bool _disposed;

    public async Task SubscribeAsync(Func<FeedEvent, Task> onEvent, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_handle is not null)
            throw new InvalidOperationException("Live feed subscription is already active.");

        var provider = clusterClient.GetStreamProvider(streamProviderName);
        var stream = provider.GetStream<EventSynapse<UiSurfacePatch>>(StreamId.Create(streamNamespace, streamId));
        _handle = await stream.SubscribeAsync(async (item, _) =>
        {
            var feedEvent = new FeedEvent(
                item.Metadata.EventId,
                item.Payload.SurfaceId,
                item.Payload.ToRevision,
                item.Payload);
            await onEvent(feedEvent);
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_handle is not null)
        {
            await _handle.UnsubscribeAsync();
            _handle = null;
        }
    }
}

public sealed class OrleansLiveFeedSubscriptionFactory(
    IClusterClient clusterClient,
    Microsoft.Extensions.Options.IOptions<GatewayFeedOptions> options) : ILiveFeedSubscriptionFactory
{
    public ILiveFeedSubscription Create()
    {
        var feed = options.Value;
        feed.EnsureValid();
        return new OrleansLiveFeedSubscription(
            clusterClient,
            feed.StreamProviderName,
            feed.StreamNamespace,
            Guid.Parse(feed.StreamId));
    }
}
