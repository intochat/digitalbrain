using Brain.Contracts;
using Brain.Gateway;
using Xunit;

namespace Brain.Tests.Gateway;

public sealed class UiWatchSessionTests
{
    [Fact]
    public async Task Reconnect_subscribes_before_reading_durable_feed()
    {
        var callOrder = new List<string>();
        var session = new UiWatchSession(
            async onLiveFrame =>
            {
                callOrder.Add("subscribe");
                await Task.CompletedTask;
            },
            async (cursor, max) =>
            {
                callOrder.Add("read");
                return new Brain.Contracts.UiFeedPage([], cursor);
            },
            surfaceId => Task.FromResult(
                new UiSurfaceSnapshot(new UiSurface(surfaceId, 0, []))));

        await session.ReconnectAsync(0, 10);

        Assert.Equal(["subscribe", "read"], callOrder);
    }

    [Fact]
    public async Task Reconnect_deduplicates_buffered_and_paged_events()
    {
        var earlierEventId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var sharedEventId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        var earlierFrame = new UiFeedFrame(
            UiFeedFrame.CurrentSchemaVersion,
            Sequence: 1,
            earlierEventId,
            UiFeedFrameTypes.Snapshot,
            new UiSurfaceSnapshot(new UiSurface("surface-1", 1, [new UiBlock("text", "earlier", [])])),
            null,
            null);

        var sharedFrame = new UiFeedFrame(
            UiFeedFrame.CurrentSchemaVersion,
            Sequence: 2,
            sharedEventId,
            UiFeedFrameTypes.Snapshot,
            new UiSurfaceSnapshot(new UiSurface("surface-1", 2, [new UiBlock("text", "shared", [])])),
            null,
            null);

        var session = new UiWatchSession(
            async onLiveFrame =>
            {
                await onLiveFrame(sharedFrame);
            },
            async (cursor, max) => new Brain.Contracts.UiFeedPage([earlierFrame, sharedFrame], NextCursor: 2),
            surfaceId => Task.FromResult(
                new UiSurfaceSnapshot(new UiSurface(surfaceId, 0, []))));

        var page = await session.ReconnectAsync(0, 10);

        Assert.Equal(2, page.Frames.Count);
        Assert.Equal([1L, 2L], page.Frames.Select(frame => frame.Sequence).ToArray());
        Assert.Equal([earlierEventId, sharedEventId], page.Frames.Select(frame => frame.EventId).ToArray());
        Assert.Equal(2, page.NextCursor);
    }

    [Fact]
    public async Task Revision_gap_fetches_snapshot()
    {
        var snapshotEventId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var patchEventId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

        var snapshotFrame = new UiFeedFrame(
            UiFeedFrame.CurrentSchemaVersion,
            Sequence: 1,
            snapshotEventId,
            UiFeedFrameTypes.Snapshot,
            new UiSurfaceSnapshot(new UiSurface("surface-1", 1, [new UiBlock("text", "base", [])])),
            null,
            null);

        var patchFrame = new UiFeedFrame(
            UiFeedFrame.CurrentSchemaVersion,
            Sequence: 2,
            patchEventId,
            UiFeedFrameTypes.Patch,
            null,
            new UiSurfacePatch("surface-1", FromRevision: 2, ToRevision: 3, []),
            null);

        var fetchedSnapshot = new UiSurfaceSnapshot(
            new UiSurface("surface-1", 5, [new UiBlock("text", "fetched", [])]));

        var fetchedSurfaceIds = new List<string>();
        var session = new UiWatchSession(
            async onLiveFrame => await Task.CompletedTask,
            async (cursor, max) => new Brain.Contracts.UiFeedPage([snapshotFrame, patchFrame], NextCursor: 2),
            surfaceId =>
            {
                fetchedSurfaceIds.Add(surfaceId);
                return Task.FromResult(fetchedSnapshot);
            });

        var page = await session.ReconnectAsync(0, 10);

        Assert.Equal(2, page.Frames.Count);
        var repaired = page.Frames[1];
        Assert.Equal(2L, repaired.Sequence);
        Assert.Equal(patchEventId, repaired.EventId);
        Assert.Equal(UiFeedFrameTypes.Snapshot, repaired.Type);
        Assert.Equal(fetchedSnapshot, repaired.Snapshot);
        Assert.Null(repaired.Patch);
        Assert.Equal(["surface-1"], fetchedSurfaceIds);
        Assert.Equal(2, page.NextCursor);
    }
}
