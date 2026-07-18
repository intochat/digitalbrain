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
    public async Task SubscribeAsync(Func<FeedEvent, Task> onEvent, CancellationToken cancellationToken = default)
    {
        var provider = clusterClient.GetStreamProvider(streamProviderName);
        var stream = provider.GetStream<EventSynapse<UiSurfacePatch>>(StreamId.Create(streamNamespace, streamId));
        await stream.SubscribeAsync(async (item, _) =>
        {
            var feedEvent = new FeedEvent(
                item.Metadata.EventId,
                item.Payload.SurfaceId,
                item.Payload.ToRevision,
                item.Payload);
            await onEvent(feedEvent);
        });
    }
}
