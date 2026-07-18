using Brain.Contracts;

namespace Brain.Gateway;

public sealed class UiFeedSession(
    ILiveFeedSubscription liveFeed,
    IDurableFeed durableFeed,
    ISurfaceOwner surfaceOwner,
    long lastKnownRevision) : IAsyncDisposable
{
    private readonly Dictionary<Guid, FeedEvent> _liveBuffer = new();
    private readonly object _gate = new();
    private long _revision = lastKnownRevision;
    private bool _disposed;

    public async Task<ReconnectResult> ReconnectAsync(int pageSize = 100, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
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
        var cursor = _revision;

        foreach (var evt in liveSnapshot.Concat(page).OrderBy(e => e.Revision).ThenBy(e => e.EventId))
        {
            if (!seen.Add(evt.EventId))
                continue;

            if (evt.Revision <= cursor)
                continue;

            if (evt.Patch is not null)
            {
                if (evt.Patch.FromRevision < cursor)
                    continue;

                if (evt.Patch.FromRevision > cursor)
                {
                    var snapshot = await surfaceOwner.GetSurfaceAsync();
                    cursor = snapshot.Surface.Revision;
                    _revision = cursor;
                    return new ReconnectResult([], snapshot, cursor);
                }
            }

            merged.Add(evt);
            if (evt.Revision > cursor)
                cursor = evt.Revision;
        }

        _revision = cursor;
        return new ReconnectResult(merged, null, cursor);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        await liveFeed.DisposeAsync();
    }
}

public sealed record ReconnectResult(
    IReadOnlyList<FeedEvent> Events,
    UiSurfaceSnapshot? Snapshot,
    long Cursor);
