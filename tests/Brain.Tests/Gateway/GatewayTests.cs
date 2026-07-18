using Brain.Contracts;
using Brain.Gateway;
using DigitalBrain.AI;
using Xunit;

namespace Brain.Tests.Gateway;

public sealed class GatewayTests
{
    [Fact]
    public async Task Ui_action_calls_surface_owner_with_expected_revision()
    {
        var owner = new RecordingSurfaceOwner();
        var gateway = new UiGatewayService(owner);
        var source = new NeuronAddress(
            DevelopmentPrincipal.OrganizationId,
            DevelopmentPrincipal.SpaceId,
            "chat.group.v1",
            "chat-1");

        var receipt = await gateway.ApplyUiActionAsync("approve-reply", expectedRevision: 7, source);

        Assert.Equal(CommandReceiptStatus.Accepted, receipt.Status);
        Assert.NotNull(owner.LastAction);
        Assert.Equal("approve-reply", owner.LastAction!.Payload.ActionId);
        Assert.Equal(7, owner.LastAction.Payload.ExpectedRevision);
        Assert.Equal(DevelopmentPrincipal.OrganizationId, owner.LastAction.Metadata.OrganizationId);
        Assert.Equal(DevelopmentPrincipal.PrincipalId, owner.LastAction.Metadata.PrincipalId);
        Assert.Equal(DevelopmentPrincipal.SpaceId, owner.LastAction.Metadata.SpaceId);
    }

    [Fact]
    public async Task Reconnect_subscribes_before_reading_durable_feed()
    {
        var calls = new List<string>();
        var live = new OrderingLiveFeed(calls);
        var durable = new OrderingDurableFeed(calls, []);
        var owner = new RecordingSurfaceOwner();
        var session = new UiFeedSession(live, durable, owner, lastKnownRevision: 0);

        await session.ReconnectAsync();

        Assert.Equal(["subscribe", "read"], calls);
    }

    [Fact]
    public async Task Reconnect_deduplicates_buffered_and_paged_events()
    {
        var sharedId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var buffered = new FeedEvent(sharedId, "group-chat", 2, Patch(1, 2));
        var pagedDuplicate = new FeedEvent(sharedId, "group-chat", 2, Patch(1, 2));
        var pagedUnique = new FeedEvent(Guid.Parse("11111111-2222-3333-4444-555555555555"), "group-chat", 3, Patch(2, 3));

        var live = new BufferingLiveFeed([buffered]);
        var durable = new StaticDurableFeed([pagedDuplicate, pagedUnique]);
        var owner = new RecordingSurfaceOwner();
        var session = new UiFeedSession(live, durable, owner, lastKnownRevision: 1);

        var result = await session.ReconnectAsync();

        Assert.Equal(2, result.Events.Count);
        Assert.Equal(sharedId, result.Events[0].EventId);
        Assert.Equal(pagedUnique.EventId, result.Events[1].EventId);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public async Task Revision_gap_fetches_snapshot()
    {
        var gapEvent = new FeedEvent(
            Guid.NewGuid(),
            "group-chat",
            10,
            new UiSurfacePatch("group-chat", FromRevision: 9, ToRevision: 10, Operations: []));
        var live = new BufferingLiveFeed([]);
        var durable = new StaticDurableFeed([gapEvent]);
        var owner = new RecordingSurfaceOwner
        {
            Snapshot = new UiSurfaceSnapshot(new UiSurface("group-chat", 10, [new UiBlock("text", "restored", [])]))
        };
        var session = new UiFeedSession(live, durable, owner, lastKnownRevision: 3);

        var result = await session.ReconnectAsync();

        Assert.NotNull(result.Snapshot);
        Assert.Equal(10, result.Snapshot!.Surface.Revision);
        Assert.Equal(1, owner.SnapshotFetchCount);
        Assert.Empty(result.Events);
    }

    [Fact]
    public void Development_principal_populates_organization_principal_and_space()
    {
        var source = new NeuronAddress(
            DevelopmentPrincipal.OrganizationId,
            DevelopmentPrincipal.SpaceId,
            "chat.group.v1",
            "chat-1");
        var metadata = GatewayCommandFactory.CreateMetadata(source);

        Assert.False(string.IsNullOrWhiteSpace(metadata.OrganizationId.Value));
        Assert.False(string.IsNullOrWhiteSpace(metadata.PrincipalId.Value));
        Assert.False(string.IsNullOrWhiteSpace(metadata.SpaceId.Value));
        Assert.Equal(DevelopmentPrincipal.OrganizationId, metadata.OrganizationId);
        Assert.Equal(DevelopmentPrincipal.PrincipalId, metadata.PrincipalId);
        Assert.Equal(DevelopmentPrincipal.SpaceId, metadata.SpaceId);
    }

    private static UiSurfacePatch Patch(long from, long to) =>
        new("group-chat", from, to, [new UiPatchOperation("replace", "/blocks/0/text", "x")]);
}

internal sealed class RecordingSurfaceOwner : ISurfaceOwner
{
    public CommandSynapse<UiActionRequest>? LastAction { get; private set; }
    public UiSurfaceSnapshot Snapshot { get; set; } =
        new(new UiSurface("group-chat", 0, []));
    public int SnapshotFetchCount { get; private set; }

    public Task<CommandReceipt> ApplyUiActionAsync(CommandSynapse<UiActionRequest> command)
    {
        LastAction = command;
        return Task.FromResult(new CommandReceipt(
            command.Metadata.CommandId,
            CommandReceiptStatus.Accepted,
            command.Payload.ExpectedRevision + 1,
            null,
            null));
    }

    public Task<UiSurfaceSnapshot> GetSurfaceAsync()
    {
        SnapshotFetchCount++;
        return Task.FromResult(Snapshot);
    }
}

internal sealed class OrderingLiveFeed(List<string> calls) : ILiveFeedSubscription
{
    public Task SubscribeAsync(Func<FeedEvent, Task> onEvent, CancellationToken cancellationToken = default)
    {
        calls.Add("subscribe");
        return Task.CompletedTask;
    }
}

internal sealed class OrderingDurableFeed(List<string> calls, IReadOnlyList<FeedEvent> page) : IDurableFeed
{
    public Task<IReadOnlyList<FeedEvent>> ReadPageAsync(long afterRevision, int pageSize, CancellationToken cancellationToken = default)
    {
        calls.Add("read");
        return Task.FromResult(page);
    }
}

internal sealed class BufferingLiveFeed(IReadOnlyList<FeedEvent> buffered) : ILiveFeedSubscription
{
    public async Task SubscribeAsync(Func<FeedEvent, Task> onEvent, CancellationToken cancellationToken = default)
    {
        foreach (var evt in buffered)
            await onEvent(evt);
    }
}

internal sealed class StaticDurableFeed(IReadOnlyList<FeedEvent> page) : IDurableFeed
{
    public Task<IReadOnlyList<FeedEvent>> ReadPageAsync(long afterRevision, int pageSize, CancellationToken cancellationToken = default) =>
        Task.FromResult(page);
}
