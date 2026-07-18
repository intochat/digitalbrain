using Brain.Contracts;
using Brain.Kernel;
using DigitalBrain.Google;
using Orleans.Streams;
using Xunit;

namespace Brain.Tests.Google;

[Collection(GmailTestCollection.Name)]
public sealed class GmailUiFeedCandidateTests
{
    private readonly GmailNeuronClusterFixture _fixture;

    public GmailUiFeedCandidateTests(GmailNeuronClusterFixture fixture) => _fixture = fixture;

    private static OrganizationId Org(string instance) => new($"org-{instance}");
    private static SpaceId Space(string instance) => new($"space-{instance}");

    private static NeuronAddress Address(string instance) =>
        new(Org(instance), Space(instance), "google.gmail.v1", instance);

    private static SynapseMetadata Meta(Guid commandId, string instance) =>
        new(
            CommandId: commandId,
            EventId: commandId,
            CausationId: commandId,
            CorrelationId: commandId,
            OrganizationId: Org(instance),
            PrincipalId: new PrincipalId("principal-1"),
            SpaceId: Space(instance),
            Source: Address(instance),
            SourceSequence: 1,
            CausalDepth: 0,
            OccurredAt: DateTimeOffset.UtcNow);

    private (IGmail Gmail, IGmailNeuronControl Control) Grain(string instance)
    {
        var key = Address(instance).ToGrainKey();
        return (
            _fixture.Cluster.GrainFactory.GetGrain<IGmail>(key),
            _fixture.Cluster.GrainFactory.GetGrain<IGmailNeuronControl>(key));
    }

    private async Task<IGmailFeedObserver> SubscribeVerticalFeedAsync(string instance)
    {
        var streamId = GmailConstants.FeedStreamIdFor(Address(instance).ToGrainKey());
        var observer = _fixture.Cluster.GrainFactory.GetGrain<IGmailFeedObserver>(streamId);
        await observer.ClearAsync();
        await observer.ReadyAsync(GmailConstants.FeedStreamNamespace, streamId);
        return observer;
    }

    private IUiFeed Feed(string instance) =>
        _fixture.Cluster.GrainFactory.GetGrain<IUiFeed>(
            UiFeedStreams.FeedKey(Org(instance), Space(instance)));

    private static async Task WaitForAsync(Func<Task<bool>> predicate, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < deadline)
        {
            if (await predicate())
                return;
            await Task.Delay(25);
        }

        throw new TimeoutException("condition not met");
    }

    private static async Task<UiFeedPage> WaitForFeedFramesAsync(IUiFeed feed, int expectedCount)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var page = await feed.ReadAsync(0, 100);
            if (page.Frames.Count >= expectedCount)
                return page;
            await Task.Delay(25);
        }

        throw new TimeoutException($"UI feed did not reach {expectedCount} frames.");
    }

    private async Task<(IGmail Gmail, IGmailNeuronControl Control)> ReactivateAsync(
        string instance,
        Guid previousToken)
    {
        await Grain(instance).Control.RequestDeactivationAsync();
        var management = _fixture.Cluster.GrainFactory.GetGrain<IManagementGrain>(0);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            await management.ForceActivationCollection(TimeSpan.Zero);
            var reloaded = Grain(instance);
            if (await reloaded.Control.GetActivationTokenAsync() != previousToken)
                return reloaded;
            await Task.Delay(50);
        }

        throw new TimeoutException($"gmail {instance} did not reactivate");
    }

    [Fact]
    public async Task UiSurface_outcome_durably_holds_common_candidate_before_publish()
    {
        var instance = "ui-candidate-pending";
        var (gmail, control) = Grain(instance);
        await control.SetAutoDrainAsync(false);
        _fixture.Mcp.ListResult = new GmailMessageListResult(2, "two");
        var commandId = Guid.NewGuid();

        await gmail.ListMessagesAsync(
            new CommandSynapse<GmailListRequest>(Meta(commandId, instance), new GmailListRequest("is:inbox", 5)));

        Assert.True(await control.GetOutboxCountAsync() >= 1);
        var pending = await control.PeekOutboxAsync();
        Assert.NotNull(pending);
        Assert.Equal(GmailFeedEvent.UiSurfaceKind, pending!.Event.Payload.Kind);
        Assert.NotNull(pending.Event.Payload.UiCandidate);
        Assert.Equal(UiFeedFrameTypes.Snapshot, pending.Event.Payload.UiCandidate!.Type);
        Assert.NotNull(pending.Event.Payload.UiCandidate.Snapshot);
        Assert.Equal(Address(instance).ToGrainKey(), pending.Event.Payload.UiCandidate.Snapshot!.Surface.SurfaceId);
        Assert.Equal((await gmail.GetSurfaceAsync()).Surface.Revision, pending.Event.Payload.UiCandidate.Snapshot.Surface.Revision);
        Assert.Contains(
            pending.Event.Payload.UiCandidate.Snapshot.Surface.Blocks,
            block => block.Kind == "text" && block.Text == "messages:2");
        Assert.Empty((await Feed(instance).ReadAsync(0, 10)).Frames);
    }

    [Fact]
    public async Task Drain_publishes_common_candidate_then_vertical_and_ui_feed_dedupes_retry()
    {
        var instance = "ui-candidate-drain";
        var (gmail, control) = Grain(instance);
        await control.SetAutoDrainAsync(false);
        var vertical = await SubscribeVerticalFeedAsync(instance);
        var feed = Feed(instance);
        await feed.EnsureSubscribedAsync();
        _fixture.Mcp.ListResult = new GmailMessageListResult(4, "four");
        var commandId = Guid.NewGuid();
        var expectedEventId = GmailConstants.OutcomeEventId(commandId, GmailFeedEvent.UiSurfaceKind);

        await gmail.ListMessagesAsync(
            new CommandSynapse<GmailListRequest>(Meta(commandId, instance), new GmailListRequest("is:inbox", 5)));
        var pending = await control.PeekOutboxAsync();
        Assert.NotNull(pending!.Event.Payload.UiCandidate);
        Assert.Equal(expectedEventId, pending.Event.Metadata.EventId);

        await control.DrainOutboxAsync();
        Assert.Equal(0, await control.GetOutboxCountAsync());

        var page = await WaitForFeedFramesAsync(feed, 1);
        var frame = Assert.Single(page.Frames);
        Assert.Equal(expectedEventId, frame.EventId);
        Assert.Equal(UiFeedFrameTypes.Snapshot, frame.Type);
        Assert.Equal(Address(instance).ToGrainKey(), frame.Snapshot!.Surface.SurfaceId);
        Assert.Equal("messages:4", frame.Snapshot.Surface.Blocks[0].Text);

        await WaitForAsync(async () =>
            (await vertical.GetEventsAsync()).Any(e => e.Kind == GmailFeedEvent.UiSurfaceKind && e.SurfaceSummary == "messages:4"));

        await control.ReplayOutboxIntentAsync(pending);
        await Task.Delay(100);
        var afterRetry = await feed.ReadAsync(0, 10);
        Assert.Single(afterRetry.Frames);
        Assert.Equal(expectedEventId, afterRetry.Frames[0].EventId);
    }

    [Fact]
    public async Task Outcome_publish_seam_keeps_candidate_and_vertical_pending_until_success()
    {
        var instance = "ui-candidate-seam";
        var (gmail, control) = Grain(instance);
        await control.SetAutoDrainAsync(false);
        var vertical = await SubscribeVerticalFeedAsync(instance);
        var feed = Feed(instance);
        await feed.EnsureSubscribedAsync();
        _fixture.Mcp.Reset();
        var commandId = Guid.NewGuid();
        var expectedEventId = GmailConstants.OutcomeEventId(commandId, GmailFeedEvent.SendCompletedKind);

        await gmail.SendMessageAsync(
            new CommandSynapse<GmailSendRequest>(
                Meta(commandId, instance),
                new GmailSendRequest("a@example.com", "Subject", "body")));
        await control.SetFailNextOutcomePublishAsync(1);

        var ex = await Assert.ThrowsAsync<BrainException>(() => control.DrainOutboxStrictAsync());
        Assert.Equal(BrainErrors.FailureSanitized, ex.Code);

        Assert.True(await control.GetOutboxCountAsync() >= 1);
        var pending = await control.PeekOutboxAsync();
        Assert.Equal(GmailFeedEvent.SendCompletedKind, pending!.Event.Payload.Kind);
        Assert.NotNull(pending.Event.Payload.UiCandidate);
        Assert.Equal(UiFeedFrameTypes.Snapshot, pending.Event.Payload.UiCandidate!.Type);
        Assert.Equal(expectedEventId, pending.Event.Metadata.EventId);
        Assert.Empty((await feed.ReadAsync(0, 10)).Frames);
        Assert.Empty(await vertical.GetEventsAsync());

        var reloaded = await ReactivateAsync(instance, await control.GetActivationTokenAsync());
        var pendingAfter = await reloaded.Control.PeekOutboxAsync();
        Assert.NotNull(pendingAfter!.Event.Payload.UiCandidate);
        Assert.Equal(expectedEventId, pendingAfter.Event.Metadata.EventId);

        await vertical.ReadyAsync(
            GmailConstants.FeedStreamNamespace,
            GmailConstants.FeedStreamIdFor(Address(instance).ToGrainKey()));
        await feed.EnsureSubscribedAsync();
        await reloaded.Control.DrainOutboxAsync();
        Assert.Equal(0, await reloaded.Control.GetOutboxCountAsync());

        var page = await WaitForFeedFramesAsync(feed, 1);
        Assert.Equal(expectedEventId, Assert.Single(page.Frames).EventId);
        await WaitForAsync(async () =>
            (await vertical.GetEventsAsync()).Any(e => e.Kind == GmailFeedEvent.SendCompletedKind && e.EffectId == commandId));
    }

    [Fact]
    public async Task Candidate_less_outcome_fails_closed_before_vertical_publish()
    {
        var instance = "ui-candidate-missing";
        var (gmail, control) = Grain(instance);
        await control.SetAutoDrainAsync(false);
        var vertical = await SubscribeVerticalFeedAsync(instance);
        _fixture.Mcp.ListResult = new GmailMessageListResult(1, "one");

        await gmail.ListMessagesAsync(
            new CommandSynapse<GmailListRequest>(
                Meta(Guid.NewGuid(), instance),
                new GmailListRequest("is:inbox", 5)));

        var pending = await control.PeekOutboxAsync();
        Assert.NotNull(pending);
        var malformed = pending! with
        {
            Event = pending.Event with
            {
                Payload = pending.Event.Payload with { UiCandidate = null }
            }
        };

        var exception = await Assert.ThrowsAsync<BrainException>(
            () => control.ReplayOutboxIntentAsync(malformed));
        Assert.Equal(BrainErrors.FailureSanitized, exception.Code);
        Assert.Empty(await vertical.GetEventsAsync());
        Assert.True(await control.GetOutboxCountAsync() >= 1);
    }

    [Fact]
    public async Task Provider_failure_enqueues_sanitized_ui_failure_candidate_without_secrets()
    {
        var instance = "ui-candidate-fail";
        var (gmail, control) = Grain(instance);
        await control.SetAutoDrainAsync(false);
        var vertical = await SubscribeVerticalFeedAsync(instance);
        var feed = Feed(instance);
        await feed.EnsureSubscribedAsync();
        _fixture.Mcp.Reset();
        _fixture.Mcp.SendException = new InvalidOperationException("provider down token=abc body=SECRET_ADDR");
        var commandId = Guid.NewGuid();
        var expectedEventId = GmailConstants.OutcomeEventId(commandId, GmailFeedEvent.SendFailedKind);

        await gmail.SendMessageAsync(
            new CommandSynapse<GmailSendRequest>(
                Meta(commandId, instance),
                new GmailSendRequest("secret@example.com", "Subject", "SECRET_BODY")));

        await control.DrainOutboxAsync();
        Assert.Equal(0, await control.GetOutboxCountAsync());

        var page = await WaitForFeedFramesAsync(feed, 1);
        var frame = Assert.Single(page.Frames);
        Assert.Equal(expectedEventId, frame.EventId);
        Assert.Equal(UiFeedFrameTypes.Failure, frame.Type);
        Assert.Equal(BrainErrors.FailureSanitized, frame.FailureCode);
        Assert.Null(frame.Snapshot);
        Assert.Null(frame.Patch);

        await WaitForAsync(async () =>
            (await vertical.GetEventsAsync()).Any(e => e.Kind == GmailFeedEvent.SendFailedKind && e.EffectId == commandId));

        Assert.DoesNotContain("SECRET", frame.FailureCode ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("token=abc", frame.FailureCode ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("secret@example.com", frame.FailureCode ?? string.Empty, StringComparison.Ordinal);
    }
}
