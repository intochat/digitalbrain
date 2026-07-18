using Brain.Contracts;

namespace Brain.Gateway;

public sealed class UiWatchSession
{
    private readonly Func<Func<UiFeedFrame, Task>, Task> _subscribeLive;
    private readonly Func<long, int, Task<UiFeedPage>> _readDurable;
    private readonly Func<string, Task<UiSurfaceSnapshot>> _fetchSnapshot;

    public UiWatchSession(
        Func<Func<UiFeedFrame, Task>, Task> subscribeLive,
        Func<long, int, Task<UiFeedPage>> readDurable,
        Func<string, Task<UiSurfaceSnapshot>> fetchSnapshot)
    {
        ArgumentNullException.ThrowIfNull(subscribeLive);
        ArgumentNullException.ThrowIfNull(readDurable);
        ArgumentNullException.ThrowIfNull(fetchSnapshot);
        _subscribeLive = subscribeLive;
        _readDurable = readDurable;
        _fetchSnapshot = fetchSnapshot;
    }

    public async Task<UiFeedPage> ReconnectAsync(long cursor, int max)
    {
        var liveBuffer = new List<UiFeedFrame>();
        var liveBufferGate = new object();

        await _subscribeLive(frame =>
        {
            lock (liveBufferGate)
            {
                liveBuffer.Add(frame);
            }

            return Task.CompletedTask;
        }).ConfigureAwait(false);

        var durablePage = await _readDurable(cursor, max).ConfigureAwait(false);

        List<UiFeedFrame> bufferedLive;
        lock (liveBufferGate)
        {
            bufferedLive = [.. liveBuffer];
        }

        var seenEventIds = new HashSet<Guid>();
        var seenSequences = new HashSet<long>();
        var surfaceRevisions = new Dictionary<string, long>(StringComparer.Ordinal);
        var merged = new List<UiFeedFrame>();

        foreach (var frame in durablePage.Frames
            .Concat(bufferedLive)
            .Where(frame => frame.Sequence > cursor)
            .OrderBy(frame => frame.Sequence))
        {
            if (seenEventIds.Contains(frame.EventId) || seenSequences.Contains(frame.Sequence))
            {
                continue;
            }

            seenEventIds.Add(frame.EventId);
            seenSequences.Add(frame.Sequence);

            if (frame.Snapshot is { } snapshot)
            {
                surfaceRevisions[snapshot.Surface.SurfaceId] = snapshot.Surface.Revision;
                merged.Add(frame);
                continue;
            }

            if (frame.Patch is { } patch)
            {
                if (surfaceRevisions.TryGetValue(patch.SurfaceId, out var revision)
                    && revision == patch.FromRevision)
                {
                    surfaceRevisions[patch.SurfaceId] = patch.ToRevision;
                    merged.Add(frame);
                    continue;
                }

                var fetched = await _fetchSnapshot(patch.SurfaceId).ConfigureAwait(false);
                surfaceRevisions[patch.SurfaceId] = fetched.Surface.Revision;
                merged.Add(frame with
                {
                    Type = UiFeedFrameTypes.Snapshot,
                    Snapshot = fetched,
                    Patch = null,
                });
                continue;
            }

            merged.Add(frame);
        }

        var nextCursor = durablePage.NextCursor;
        if (merged.Count > 0)
        {
            nextCursor = Math.Max(nextCursor, merged[^1].Sequence);
        }

        return new UiFeedPage(merged, nextCursor);
    }
}
