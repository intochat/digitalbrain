using Brain.Contracts;

namespace Brain.Gateway;

public sealed class UiFeedSession(
    ILiveFeedSubscription liveFeed,
    IDurableFeed durableFeed,
    ISurfaceOwner surfaceOwner,
    long lastKnownRevision)
{
    private readonly Dictionary<Guid, FeedEvent> _liveBuffer = new();
    private readonly object _gate = new();
    private long _revision = lastKnownRevision;

    public async Task<ReconnectResult> ReconnectAsync(int pageSize = 100, CancellationToken cancellationToken = default)
    {
        await liveFeed.SubscribeAsync(OnLiveEventAsync, cancellationToken);
        var page = await durableFeed.ReadPageAsync(_revision, pageSize, cancellationToken);
        return await MergeAsync(page);
    }

    private Task OnLiveEventAsync(FeedEvent feedEvent)
    {
        lock (_gate)
            _liveBuffer[feedEvent.EventId] = feedEvent;
        return Task.CompletedTask;
    }

    private async Task<ReconnectResult> MergeAsync(IReadOnlyList<FeedEvent> page)
    {
        List<FeedEvent> liveSnapshot;
        lock (_gate)
            liveSnapshot = _liveBuffer.Values.ToList();

        var merged = new List<FeedEvent>();
        var seen = new HashSet<Guid>();

        foreach (var evt in liveSnapshot.Concat(page).OrderBy(e => e.Revision))
        {
            if (!seen.Add(evt.EventId))
                continue;

            if (evt.Patch is not null && evt.Patch.FromRevision > _revision)
            {
                var snapshot = await surfaceOwner.GetSurfaceAsync();
                _revision = snapshot.Surface.Revision;
                return new ReconnectResult([], snapshot, _revision);
            }

            merged.Add(evt);
            _revision = evt.Revision;
        }

        return new ReconnectResult(merged, null, _revision);
    }
}

public sealed record ReconnectResult(
    IReadOnlyList<FeedEvent> Events,
    UiSurfaceSnapshot? Snapshot,
    long Cursor);
