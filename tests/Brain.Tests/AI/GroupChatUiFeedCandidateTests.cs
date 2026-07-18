using Brain.Client;
using Brain.Contracts;
using DigitalBrain.AI;
using Orleans.Runtime;
using Xunit;

namespace Brain.Tests.AI;

[Collection(AiTestCollection.Name)]
public sealed class GroupChatUiFeedCandidateTests
{
    private readonly AiClusterFixture _fixture;

    public GroupChatUiFeedCandidateTests(AiClusterFixture fixture)
    {
        _fixture = fixture;
        _fixture.GptClient.Reset();
        _fixture.GrokClient.Reset();
    }

    private static OrganizationId Org(string scope) => new($"org-{scope}");
    private static SpaceId Space(string scope) => new($"space-{scope}");

    private static SynapseMetadata Meta(Guid commandId, string scope) =>
        new(
            CommandId: commandId,
            EventId: commandId,
            CausationId: commandId,
            CorrelationId: commandId,
            OrganizationId: Org(scope),
            PrincipalId: new PrincipalId("principal-1"),
            SpaceId: Space(scope),
            Source: new NeuronAddress(Org(scope), Space(scope), "chat.group.v1", "source"),
            SourceSequence: 0,
            CausalDepth: 0,
            OccurredAt: DateTimeOffset.UtcNow);

    private IGpt56Turn Gpt(string scope, string name) =>
        _fixture.Cluster.GrainFactory.GetGrain<IGpt56Turn>(
            NeuronIdentity.Derive(typeof(IGpt56), Org(scope), Space(scope), name));

    private IGrok45Turn Grok(string scope, string name) =>
        _fixture.Cluster.GrainFactory.GetGrain<IGrok45Turn>(
            NeuronIdentity.Derive(typeof(IGrok45), Org(scope), Space(scope), name));

    private IGroupChatControl Chat(string scope) =>
        _fixture.Cluster.GrainFactory.GetGrain<IGroupChatControl>(
            NeuronIdentity.Derive(typeof(IGroupChat), Org(scope), Space(scope), scope));

    private static string ChatKey(string scope) =>
        NeuronIdentity.Derive(typeof(IGroupChat), Org(scope), Space(scope), scope);

    private IUiFeed Feed(string scope) =>
        _fixture.Cluster.GrainFactory.GetGrain<IUiFeed>(
            UiFeedStreams.FeedKey(Org(scope), Space(scope)));

    private static async Task WaitForStepCountAsync(IGroupChatControl chat, int minimumSteps, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var diagnostics = await chat.GetDiagnosticsAsync();
            if (diagnostics.StepCount >= minimumSteps)
                return;
            if (!string.IsNullOrWhiteSpace(diagnostics.LastFailureMessage))
                throw new InvalidOperationException($"Step failed: {diagnostics.LastFailureMessage}");
            await Task.Delay(50);
        }

        var final = await chat.GetDiagnosticsAsync();
        throw new TimeoutException(
            $"Timed out waiting for step count {minimumSteps}. actual={final.StepCount}, outbox={final.OutboxCount}");
    }

    private static async Task WaitForFailureAsync(IGroupChatControl chat, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var diagnostics = await chat.GetDiagnosticsAsync();
            if (!string.IsNullOrWhiteSpace(diagnostics.LastFailureMessage))
                return;
            await Task.Delay(50);
        }

        throw new TimeoutException("Timed out waiting for durable failure.");
    }

    private static async Task<UiFeedPage> WaitForFeedFramesAsync(IUiFeed feed, int expectedCount)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var page = await feed.ReadAsync(0, 100);
            if (page.Frames.Count >= expectedCount)
                return page;
            await Task.Delay(25);
        }

        throw new TimeoutException($"UI feed did not reach {expectedCount} frames.");
    }

    private static async Task<UiFeedPage> WaitForCancelledFrameAsync(IUiFeed feed)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var page = await feed.ReadAsync(0, 100);
            if (page.Frames.Any(frame =>
                    frame.Type == UiFeedFrameTypes.Snapshot
                    && frame.Snapshot is not null
                    && frame.Snapshot.Surface.Blocks.Any(block =>
                        block.Kind == "status" && block.Text == "cancelled")))
            {
                return page;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("UI feed did not receive cancelled snapshot.");
    }

    private static async Task<UiFeedPage> WaitForFailureFrameAsync(IUiFeed feed)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var page = await feed.ReadAsync(0, 100);
            if (page.Frames.Any(frame => frame.Type == UiFeedFrameTypes.Failure))
                return page;
            await Task.Delay(25);
        }

        throw new TimeoutException("UI feed did not receive failure frame.");
    }

    private async Task<IGroupChatControl> ReactivateAsync(string scope, Guid priorToken)
    {
        var chat = Chat(scope);
        await chat.RequestDeactivationAsync();
        var management = _fixture.Cluster.GrainFactory.GetGrain<IManagementGrain>(0);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            await management.ForceActivationCollection(TimeSpan.Zero);
            var next = Chat(scope);
            var diagnostics = await next.GetDiagnosticsAsync();
            if (diagnostics.ActivationToken != priorToken)
                return next;
            await Task.Delay(50);
        }

        throw new TimeoutException("grain did not change activation token");
    }

    [Fact]
    public async Task Start_enqueues_ui_candidate_and_step_intents_with_distinct_event_ids()
    {
        const string scope = "gc-ui-start";
        var chat = Chat(scope);
        await chat.SetAutoDrainAsync(false);
        var gpt = Gpt(scope, "gpt");
        var grok = Grok(scope, "grok");

        await chat.StartDiscussionAsync(new CommandSynapse<StartDiscussion>(
            Meta(Guid.NewGuid(), scope),
            new StartDiscussion(
                "topic-ui-start",
                ((IAddressable)gpt).GetGrainId().Key.ToString()!,
                ((IAddressable)grok).GetGrainId().Key.ToString()!)));

        var diagnostics = await chat.GetDiagnosticsAsync();
        Assert.Equal(2, diagnostics.OutboxCount);

        var head = await chat.PeekOutboxEventAsync();
        Assert.NotNull(head);
        Assert.True(head!.Payload.IsUiIntent);
        Assert.False(head.Payload.IsStepIntent);
        Assert.Equal(GroupChatStepEvent.UiKind, head.Payload.IntentKind);
        Assert.NotNull(head.Payload.Candidate);
        Assert.Equal(UiFeedFrameTypes.Snapshot, head.Payload.Candidate!.Type);
        Assert.Equal(ChatKey(scope), head.Payload.Candidate.Snapshot!.Surface.SurfaceId);
        Assert.Equal(1, head.Payload.Candidate.Snapshot.Surface.Revision);
        Assert.Contains(head.Payload.Candidate.Snapshot.Surface.Blocks, block => block.Kind == "topic");

        var step = await chat.PeekStepOutboxEventAsync();
        Assert.NotNull(step);
        Assert.True(step!.Payload.IsStepIntent);
        Assert.False(step.Payload.IsUiIntent);
        Assert.Equal(GroupChatStepEvent.StepKind, step.Payload.IntentKind);
        Assert.Null(step.Payload.Candidate);
        Assert.NotEqual(head.Metadata.EventId, step.Metadata.EventId);
        Assert.Empty((await Feed(scope).ReadAsync(0, 10)).Frames);
        Assert.Equal(0, diagnostics.StepCount);
    }

    [Fact]
    public async Task Drain_delivers_initial_snapshot_to_ui_feed_without_treating_ui_as_step()
    {
        const string scope = "gc-ui-drain";
        var chat = Chat(scope);
        await chat.SetAutoDrainAsync(false);
        var feed = Feed(scope);
        await feed.EnsureSubscribedAsync();
        var gpt = Gpt(scope, "gpt");
        var grok = Grok(scope, "grok");

        await chat.StartDiscussionAsync(new CommandSynapse<StartDiscussion>(
            Meta(Guid.NewGuid(), scope),
            new StartDiscussion(
                "topic-ui-drain",
                ((IAddressable)gpt).GetGrainId().Key.ToString()!,
                ((IAddressable)grok).GetGrainId().Key.ToString()!)));

        var uiEventId = (await chat.PeekOutboxEventAsync())!.Metadata.EventId;
        await chat.DrainOutboxAsync();
        await WaitForStepCountAsync(chat, 1, TimeSpan.FromSeconds(15));

        var page = await WaitForFeedFramesAsync(feed, 1);
        Assert.Contains(page.Frames, frame => frame.EventId == uiEventId && frame.Type == UiFeedFrameTypes.Snapshot);
        Assert.Equal(1, (await chat.GetDiagnosticsAsync()).StepCount);
        Assert.True((await chat.GetDiagnosticsAsync()).OutboxCount >= 2);
    }

    [Fact]
    public async Task Successful_step_enqueues_ui_candidate_plus_next_step_and_ui_feed_dedupes_redelivery()
    {
        const string scope = "gc-ui-step";
        var chat = Chat(scope);
        await chat.SetAutoDrainAsync(false);
        var feed = Feed(scope);
        await feed.EnsureSubscribedAsync();
        var gpt = Gpt(scope, "gpt");
        var grok = Grok(scope, "grok");

        await chat.StartDiscussionAsync(new CommandSynapse<StartDiscussion>(
            Meta(Guid.NewGuid(), scope),
            new StartDiscussion(
                "topic-ui-step",
                ((IAddressable)gpt).GetGrainId().Key.ToString()!,
                ((IAddressable)grok).GetGrainId().Key.ToString()!)));
        await chat.DrainOutboxAsync();
        await WaitForStepCountAsync(chat, 1, TimeSpan.FromSeconds(15));

        var afterStep = await chat.GetDiagnosticsAsync();
        Assert.Equal(2, afterStep.UiRevision);
        Assert.True(afterStep.OutboxCount >= 2);

        var uiHead = await chat.PeekOutboxEventAsync();
        Assert.True(uiHead!.Payload.IsUiIntent);
        Assert.Equal(2, uiHead.Payload.Candidate!.Snapshot!.Surface.Revision);
        var stepEventId = (await chat.PeekStepOutboxEventAsync())!.Metadata.EventId;
        Assert.NotEqual(uiHead.Metadata.EventId, stepEventId);

        await chat.DrainOutboxAsync();
        await WaitForStepCountAsync(chat, 2, TimeSpan.FromSeconds(15));

        var page = await WaitForFeedFramesAsync(feed, 2);
        Assert.Contains(page.Frames, frame => frame.EventId == uiHead.Metadata.EventId);

        await chat.PublishStepEventAsync(uiHead);
        await Task.Delay(200);
        Assert.Equal(2, (await chat.GetDiagnosticsAsync()).StepCount);

        var framesBefore = (await feed.ReadAsync(0, 100)).Frames.Count;
        await chat.PublishUiCandidateEventAsync(uiHead);
        await Task.Delay(200);
        Assert.Equal(framesBefore, (await feed.ReadAsync(0, 100)).Frames.Count);
    }

    [Fact]
    public async Task Cancel_enqueues_ui_snapshot_candidate_without_extra_step()
    {
        const string scope = "gc-ui-cancel";
        var chat = Chat(scope);
        await chat.SetAutoDrainAsync(false);
        var feed = Feed(scope);
        await feed.EnsureSubscribedAsync();
        var gpt = Gpt(scope, "gpt");
        var grok = Grok(scope, "grok");

        await chat.StartDiscussionAsync(new CommandSynapse<StartDiscussion>(
            Meta(Guid.NewGuid(), scope),
            new StartDiscussion(
                "topic-ui-cancel",
                ((IAddressable)gpt).GetGrainId().Key.ToString()!,
                ((IAddressable)grok).GetGrainId().Key.ToString()!)));
        await chat.DrainOutboxAsync();
        await WaitForStepCountAsync(chat, 1, TimeSpan.FromSeconds(15));

        var diagnostics = await chat.GetDiagnosticsAsync();
        await chat.ApplyUiActionAsync(new CommandSynapse<UiActionRequest>(
            Meta(Guid.NewGuid(), scope),
            new UiActionRequest(GroupChatNeuron.CancelActionId, diagnostics.UiRevision)));

        var afterCancel = await chat.GetDiagnosticsAsync();
        Assert.True(afterCancel.IsCancelled);
        Assert.True(afterCancel.OutboxCount >= 1);
        Assert.Equal("cancelled", (await chat.GetSurfaceAsync()).Surface.Blocks
            .Single(block => block.Kind == "status").Text);

        await chat.DrainOutboxAsync();
        var page = await WaitForCancelledFrameAsync(feed);
        Assert.Contains(
            page.Frames,
            frame => frame.Type == UiFeedFrameTypes.Snapshot
                && frame.Snapshot is not null
                && frame.Snapshot.Surface.Blocks.Any(block =>
                    block.Kind == "status" && block.Text == "cancelled"));
        Assert.Equal(1, (await chat.GetDiagnosticsAsync()).StepCount);
    }

    [Fact]
    public async Task Provider_failure_enqueues_sanitized_ui_failure_candidate_to_ui_feed()
    {
        const string scope = "gc-ui-fail";
        var chat = Chat(scope);
        await chat.SetAutoDrainAsync(false);
        var feed = Feed(scope);
        await feed.EnsureSubscribedAsync();
        var gpt = Gpt(scope, "gpt");
        var grok = Grok(scope, "grok");
        _fixture.GptClient.FailNextWith("secret api key sk-test-123 prompt leaked");

        await chat.StartDiscussionAsync(new CommandSynapse<StartDiscussion>(
            Meta(Guid.NewGuid(), scope),
            new StartDiscussion(
                "topic-ui-fail",
                ((IAddressable)gpt).GetGrainId().Key.ToString()!,
                ((IAddressable)grok).GetGrainId().Key.ToString()!)));
        await chat.DrainOutboxAsync();
        await WaitForFailureAsync(chat, TimeSpan.FromSeconds(15));

        var failureUi = await chat.PeekOutboxEventAsync();
        Assert.NotNull(failureUi);
        Assert.True(failureUi!.Payload.IsUiIntent);
        Assert.Equal(UiFeedFrameTypes.Failure, failureUi.Payload.Candidate!.Type);
        Assert.Equal(BrainErrors.FailureSanitized, failureUi.Payload.Candidate.FailureCode);
        Assert.Null(failureUi.Payload.Candidate.Snapshot);

        await chat.DrainOutboxAsync();
        var page = await WaitForFailureFrameAsync(feed);
        var frame = page.Frames.Single(f => f.Type == UiFeedFrameTypes.Failure);
        Assert.Equal(BrainErrors.FailureSanitized, frame.FailureCode);
        Assert.DoesNotContain("sk-test", frame.FailureCode ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Restart_preserves_pending_ui_candidate_and_ui_feed_dedupes_redelivery()
    {
        const string scope = "gc-ui-restart";
        var chat = Chat(scope);
        await chat.SetAutoDrainAsync(false);
        var feed = Feed(scope);
        await feed.EnsureSubscribedAsync();
        var gpt = Gpt(scope, "gpt");
        var grok = Grok(scope, "grok");

        await chat.StartDiscussionAsync(new CommandSynapse<StartDiscussion>(
            Meta(Guid.NewGuid(), scope),
            new StartDiscussion(
                "topic-ui-restart",
                ((IAddressable)gpt).GetGrainId().Key.ToString()!,
                ((IAddressable)grok).GetGrainId().Key.ToString()!)));

        var before = await chat.GetDiagnosticsAsync();
        Assert.Equal(2, before.OutboxCount);
        var pendingUi = await chat.PeekOutboxEventAsync();
        Assert.True(pendingUi!.Payload.IsUiIntent);
        var pendingEventId = pendingUi.Metadata.EventId;

        var reloaded = await ReactivateAsync(scope, before.ActivationToken);
        await reloaded.SetAutoDrainAsync(false);
        var after = await reloaded.GetDiagnosticsAsync();
        Assert.Equal(2, after.OutboxCount);
        Assert.Equal(pendingEventId, (await reloaded.PeekOutboxEventAsync())!.Metadata.EventId);

        await reloaded.DrainOutboxAsync();
        await WaitForStepCountAsync(reloaded, 1, TimeSpan.FromSeconds(15));
        var page = await WaitForFeedFramesAsync(feed, 1);
        Assert.Contains(page.Frames, frame => frame.EventId == pendingEventId);

        await reloaded.PublishUiCandidateEventAsync(pendingUi);
        await Task.Delay(200);
        Assert.Equal(1, (await feed.ReadAsync(0, 100)).Frames.Count(frame => frame.EventId == pendingEventId));
    }
}
