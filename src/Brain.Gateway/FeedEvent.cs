using Brain.Contracts;

namespace Brain.Gateway;

[GenerateSerializer, Alias("brain.gateway.feed-event.v1")]
public sealed record FeedEvent(
    [property: Id(0)] Guid EventId,
    [property: Id(1)] string SurfaceId,
    [property: Id(2)] long Revision,
    [property: Id(3)] UiSurfacePatch? Patch);
