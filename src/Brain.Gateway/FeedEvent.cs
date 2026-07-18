using Brain.Contracts;

namespace Brain.Gateway;

public sealed record FeedEvent(
    Guid EventId,
    string SurfaceId,
    long Revision,
    UiSurfacePatch? Patch);
