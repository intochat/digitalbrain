using System.Diagnostics;
using System.Text.RegularExpressions;
using Brain.Client;
using Brain.Contracts;
using DigitalBrain.AI;
using Orleans.Runtime;
using Xunit;

namespace Brain.Tests.AI;

[Collection(AiTestCollection.Name)]
public sealed class GroupChatNeuronTests
{
    private readonly AiClusterFixture _fixture;

    public GroupChatNeuronTests(AiClusterFixture fixture)
    {
        _fixture = fixture;
        _fixture.GptClient.Reset();
        _fixture.GrokClient.Reset();
    }

    private static SynapseMetadata Meta(Guid commandId) =>
        new(
            CommandId: commandId,
            EventId: commandId,
            CausationId: commandId,
            CorrelationId: commandId,
            OrganizationId: new OrganizationId("org-1"),
            PrincipalId: new PrincipalId("principal-1"),
            SpaceId: new SpaceId("space-1"),
            Source: new NeuronAddress(new OrganizationId("org-1"), new SpaceId("space-1"), "chat.group.v1", "source"),
            SourceSequence: 0,
            CausalDepth: 0,
            OccurredAt: DateTimeOffset.UtcNow);

    private IGpt56Turn Gpt(string instance) =>
        _fixture.Cluster.GrainFactory.GetGrain<IGpt56Turn>(
            NeuronIdentity.Derive(typeof(IGpt56), new OrganizationId("org-1"), new SpaceId("space-1"), instance));

    private IGrok45Turn Grok(string instance) =>
        _fixture.Cluster.GrainFactory.GetGrain<IGrok45Turn>(
            NeuronIdentity.Derive(typeof(IGrok45), new OrganizationId("org-1"), new SpaceId("space-1"), instance));

    private IGroupChatControl Chat(string instance) =>
        _fixture.Cluster.GrainFactory.GetGrain<IGroupChatControl>(
            NeuronIdentity.Derive(typeof(IGroupChat), new OrganizationId("org-1"), new SpaceId("space-1"), instance));

    private static string ChatKey(string instance) =>
        NeuronIdentity.Derive(typeof(IGroupChat), new OrganizationId("org-1"), new SpaceId("space-1"), instance);

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
            $"Timed out waiting for step count {minimumSteps}. actual={final.StepCount}, outbox={final.OutboxCount}, failure={final.LastFailureMessage}");
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

    private async Task<IGroupChatControl> ReactivateAsync(string instance, Guid priorToken)
    {
        var chat = Chat(instance);
        await chat.RequestDeactivationAsync();
        var management = _fixture.Cluster.GrainFactory.GetGrain<IManagementGrain>(0);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            await management.ForceActivationCollection(TimeSpan.Zero);
            var next = Chat(instance);
            var diagnostics = await next.GetDiagnosticsAsync();
            if (diagnostics.ActivationToken != priorToken)
                return next;
            await Task.Delay(50);
        }

        throw new TimeoutException("grain did not change activation token");
    }

    [Fact]
    public async Task Gpt56_and_Grok45_use_distinct_grain_identities_and_state()
    {
        var gpt = Gpt("gpt-distinct");
        var grok = Grok("grok-distinct");

        var gptId = await gpt.GetIdentityAsync();
        var grokId = await grok.GetIdentityAsync();
        Assert.NotEqual(gptId, grokId);
        Assert.Contains("agent.gpt56.v1", gptId, StringComparison.Ordinal);
        Assert.Contains("agent.grok45.v1", grokId, StringComparison.Ordinal);

        var gptCommandId = Guid.NewGuid();
        var grokCommandId = Guid.NewGuid();
        await gpt.CompleteTurnAsync(new CommandSynapse<AgentTurnRequest>(
            Meta(gptCommandId),
            new AgentTurnRequest("req-gpt", "hello-gpt")));
        await grok.CompleteTurnAsync(new CommandSynapse<AgentTurnRequest>(
            Meta(grokCommandId),
            new AgentTurnRequest("req-grok", "hello-grok")));

        var gptState = await gpt.GetTurnStateAsync();
        var grokState = await grok.GetTurnStateAsync();
        Assert.Equal("req-gpt", gptState.LastRequestId);
        Assert.Equal("req-grok", grokState.LastRequestId);
        Assert.Equal(1, gptState.CompletedTurnCount);
        Assert.Equal(1, grokState.CompletedTurnCount);
        Assert.Equal(1, _fixture.GptClient.InvocationCount);
        Assert.Equal(1, _fixture.GrokClient.InvocationCount);
    }

    [Fact]
    public async Task Start_discussion_commits_before_first_participant_step()
    {
        var chat = Chat("start-before-step");
        await chat.SetAutoDrainAsync(false);
        var gpt = Gpt("gpt-start");
        var grok = Grok("grok-start");

        var gptBefore = _fixture.GptClient.InvocationCount;
        var grokBefore = _fixture.GrokClient.InvocationCount;

        var commandId = Guid.NewGuid();
        var receipt = await chat.StartDiscussionAsync(new CommandSynapse<StartDiscussion>(
            Meta(commandId),
            new StartDiscussion(
                "topic-start",
                ((IAddressable)gpt).GetGrainId().Key.ToString()!,
                ((IAddressable)grok).GetGrainId().Key.ToString()!)));

        Assert.Equal(CommandReceiptStatus.Accepted, receipt.Status);
        var diagnostics = await chat.GetDiagnosticsAsync();
        Assert.Equal(0, diagnostics.StepCount);
        Assert.Equal(1, diagnostics.TranscriptCount);
        Assert.True(diagnostics.OutboxCount >= 1);
        Assert.Equal(gptBefore, _fixture.GptClient.InvocationCount);
        Assert.Equal(grokBefore, _fixture.GrokClient.InvocationCount);
        Assert.Null(diagnostics.CheckpointId);
    }

    [Fact]
    public async Task One_reaction_commits_one_participant_response()
    {
        var chat = Chat("one-response");
        await chat.SetAutoDrainAsync(false);
        var gpt = Gpt("gpt-one-response");
        var grok = Grok("grok-one-response");
        var gptBefore = _fixture.GptClient.InvocationCount;
        var grokBefore = _fixture.GrokClient.InvocationCount;

        await chat.StartDiscussionAsync(new CommandSynapse<StartDiscussion>(
            Meta(Guid.NewGuid()),
            new StartDiscussion(
                "topic-one",
                ((IAddressable)gpt).GetGrainId().Key.ToString()!,
                ((IAddressable)grok).GetGrainId().Key.ToString()!)));

        await chat.DrainOutboxAsync();
        await WaitForStepCountAsync(chat, 1, TimeSpan.FromSeconds(15));

        var diagnostics = await chat.GetDiagnosticsAsync();
        Assert.Equal(1, diagnostics.StepCount);
        Assert.Contains(diagnostics.TranscriptTexts, text => text.Contains("gpt-reply", StringComparison.Ordinal));
        Assert.Equal(gptBefore + 1, _fixture.GptClient.InvocationCount);
        Assert.Equal(grokBefore, _fixture.GrokClient.InvocationCount);
        Assert.True(diagnostics.OutboxCount >= 2);
    }

    [Fact]
    public async Task One_reaction_commits_one_checkpoint()
    {
        var chat = Chat("one-checkpoint");
        await chat.SetAutoDrainAsync(false);
        var gpt = Gpt("gpt-checkpoint");
        var grok = Grok("grok-checkpoint");

        await chat.StartDiscussionAsync(new CommandSynapse<StartDiscussion>(
            Meta(Guid.NewGuid()),
            new StartDiscussion(
                "topic-checkpoint",
                ((IAddressable)gpt).GetGrainId().Key.ToString()!,
                ((IAddressable)grok).GetGrainId().Key.ToString()!)));
        await chat.DrainOutboxAsync();
        await WaitForStepCountAsync(chat, 1, TimeSpan.FromSeconds(15));

        var diagnostics = await chat.GetDiagnosticsAsync();
        Assert.False(string.IsNullOrWhiteSpace(diagnostics.CheckpointId));
        Assert.False(string.IsNullOrWhiteSpace(diagnostics.CheckpointSessionId));
        Assert.True(diagnostics.HasCheckpointJson);
        Assert.True(diagnostics.CheckpointJsonLength > 0);
        Assert.Equal(1, diagnostics.StepCount);

        var surface = await chat.GetSurfaceAsync();
        Assert.Contains(surface.Surface.Blocks, block => block.Kind == "checkpoint" && block.Text == diagnostics.CheckpointId);
    }

    [Fact]
    public async Task One_reaction_commits_one_UiSurface_revision()
    {
        var chat = Chat("one-ui");
        await chat.SetAutoDrainAsync(false);
        var gpt = Gpt("gpt-ui");
        var grok = Grok("grok-ui");

        await chat.StartDiscussionAsync(new CommandSynapse<StartDiscussion>(
            Meta(Guid.NewGuid()),
            new StartDiscussion(
                "topic-ui",
                ((IAddressable)gpt).GetGrainId().Key.ToString()!,
                ((IAddressable)grok).GetGrainId().Key.ToString()!)));

        var afterStart = await chat.GetDiagnosticsAsync();
        Assert.Equal(1, afterStart.UiRevision);

        await chat.DrainOutboxAsync();
        await WaitForStepCountAsync(chat, 1, TimeSpan.FromSeconds(15));

        var afterStep = await chat.GetDiagnosticsAsync();
        Assert.Equal(2, afterStep.UiRevision);
        var surface = await chat.GetSurfaceAsync();
        Assert.Equal(2, surface.Surface.Revision);
        Assert.Contains(surface.Surface.Blocks, block => block.Kind == "message" && block.Text.Contains("gpt-reply", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Next_step_occurs_in_a_later_Orleans_turn()
    {
        var chat = Chat("later-turn");
        await chat.SetAutoDrainAsync(false);
        var gpt = Gpt("gpt-later");
        var grok = Grok("grok-later");
        var gptBefore = _fixture.GptClient.InvocationCount;
        var grokBefore = _fixture.GrokClient.InvocationCount;

        await chat.StartDiscussionAsync(new CommandSynapse<StartDiscussion>(
            Meta(Guid.NewGuid()),
            new StartDiscussion(
                "topic-later",
                ((IAddressable)gpt).GetGrainId().Key.ToString()!,
                ((IAddressable)grok).GetGrainId().Key.ToString()!)));

        var tokenAfterStart = (await chat.GetDiagnosticsAsync()).ActivationToken;
        await chat.DrainOutboxAsync();
        await WaitForStepCountAsync(chat, 1, TimeSpan.FromSeconds(15));
        var afterFirst = await chat.GetDiagnosticsAsync();
        Assert.Equal(1, afterFirst.StepCount);
        Assert.Equal(gptBefore + 1, _fixture.GptClient.InvocationCount);
        Assert.Equal(grokBefore, _fixture.GrokClient.InvocationCount);
        Assert.True(afterFirst.OutboxCount >= 2);

        await chat.DrainOutboxAsync();
        await WaitForStepCountAsync(chat, 2, TimeSpan.FromSeconds(15));
        var afterSecond = await chat.GetDiagnosticsAsync();
        Assert.Equal(2, afterSecond.StepCount);
        Assert.Equal(gptBefore + 1, _fixture.GptClient.InvocationCount);
        Assert.Equal(grokBefore + 1, _fixture.GrokClient.InvocationCount);
        Assert.Contains(afterSecond.TranscriptTexts, text => text.Contains("grok-reply", StringComparison.Ordinal));
        Assert.Equal(tokenAfterStart, afterSecond.ActivationToken);
    }

    [Fact]
    public async Task Duplicate_step_event_does_not_call_participant_twice()
    {
        var chat = Chat("dup-step");
        await chat.SetAutoDrainAsync(false);
        var gpt = Gpt("gpt-dup");
        var grok = Grok("grok-dup");

        await chat.StartDiscussionAsync(new CommandSynapse<StartDiscussion>(
            Meta(Guid.NewGuid()),
            new StartDiscussion(
                "topic-dup",
                ((IAddressable)gpt).GetGrainId().Key.ToString()!,
                ((IAddressable)grok).GetGrainId().Key.ToString()!)));

        var pending = await chat.PeekStepOutboxEventAsync();
        Assert.NotNull(pending);
        Assert.True(pending!.Payload.IsStepIntent);

        await chat.DrainOutboxAsync();
        await WaitForStepCountAsync(chat, 1, TimeSpan.FromSeconds(15));

        var gptAfterFirst = _fixture.GptClient.InvocationCount;
        var stepAfterFirst = (await chat.GetDiagnosticsAsync()).StepCount;
        Assert.Equal(1, stepAfterFirst);

        await chat.PublishStepEventAsync(pending!);
        await Task.Delay(250);

        Assert.Equal(gptAfterFirst, _fixture.GptClient.InvocationCount);
        Assert.Equal(1, (await chat.GetDiagnosticsAsync()).StepCount);
    }

    [Fact]
    public async Task Reactivation_restores_transcript_checkpoint_and_next_participant()
    {
        var chat = Chat("reactivate");
        await chat.SetAutoDrainAsync(false);
        var gpt = Gpt("gpt-reactivate");
        var grok = Grok("grok-reactivate");

        await chat.StartDiscussionAsync(new CommandSynapse<StartDiscussion>(
            Meta(Guid.NewGuid()),
            new StartDiscussion(
                "topic-reactivate",
                ((IAddressable)gpt).GetGrainId().Key.ToString()!,
                ((IAddressable)grok).GetGrainId().Key.ToString()!)));
        await chat.DrainOutboxAsync();
        await WaitForStepCountAsync(chat, 1, TimeSpan.FromSeconds(15));

        var before = await chat.GetDiagnosticsAsync();
        Assert.Equal(1, before.StepCount);
        Assert.False(string.IsNullOrWhiteSpace(before.CheckpointId));
        Assert.True(before.HasCheckpointJson);
        Assert.True(before.CheckpointJsonLength > 0);
        var token = before.ActivationToken;

        var reloaded = await ReactivateAsync("reactivate", token);
        var next = await reloaded.GetDiagnosticsAsync();
        Assert.Equal(1, next.StepCount);
        Assert.Equal(before.CheckpointId, next.CheckpointId);
        Assert.Equal(before.CheckpointSessionId, next.CheckpointSessionId);
        Assert.Equal(before.ParticipantCursor, next.ParticipantCursor);
        Assert.Equal(before.TranscriptTexts, next.TranscriptTexts);
        Assert.True(next.HasCheckpointJson);
        Assert.Equal(before.CheckpointJsonLength, next.CheckpointJsonLength);
        Assert.True(next.OutboxCount >= 2);

        var grokBeforeSecond = _fixture.GrokClient.InvocationCount;
        await reloaded.SetAutoDrainAsync(false);
        await reloaded.DrainOutboxAsync();
        await WaitForStepCountAsync(reloaded, 2, TimeSpan.FromSeconds(15));
        var after = await reloaded.GetDiagnosticsAsync();
        Assert.Equal(2, after.StepCount);
        Assert.Equal(grokBeforeSecond + 1, _fixture.GrokClient.InvocationCount);
        Assert.Contains(after.TranscriptTexts, text => text.Contains("grok-reply", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Cancel_prevents_later_steps()
    {
        var chat = Chat("cancel");
        await chat.SetAutoDrainAsync(false);
        var gpt = Gpt("gpt-cancel");
        var grok = Grok("grok-cancel");

        await chat.StartDiscussionAsync(new CommandSynapse<StartDiscussion>(
            Meta(Guid.NewGuid()),
            new StartDiscussion(
                "topic-cancel",
                ((IAddressable)gpt).GetGrainId().Key.ToString()!,
                ((IAddressable)grok).GetGrainId().Key.ToString()!)));
        await chat.DrainOutboxAsync();
        await WaitForStepCountAsync(chat, 1, TimeSpan.FromSeconds(15));

        var diagnostics = await chat.GetDiagnosticsAsync();
        var gptBeforeCancelDrain = _fixture.GptClient.InvocationCount;
        var grokBeforeCancelDrain = _fixture.GrokClient.InvocationCount;

        await chat.ApplyUiActionAsync(new CommandSynapse<UiActionRequest>(
            Meta(Guid.NewGuid()),
            new UiActionRequest(GroupChatNeuron.CancelActionId, diagnostics.UiRevision)));

        await chat.DrainOutboxAsync();
        await Task.Delay(200);

        var after = await chat.GetDiagnosticsAsync();
        Assert.True(after.IsCancelled);
        Assert.Equal(1, after.StepCount);
        Assert.Equal(gptBeforeCancelDrain, _fixture.GptClient.InvocationCount);
        Assert.Equal(grokBeforeCancelDrain, _fixture.GrokClient.InvocationCount);
    }

    [Fact]
    public async Task Provider_failure_is_durable_sanitized_and_visible_in_UiSurface()
    {
        var chat = Chat("provider-fail");
        await chat.SetAutoDrainAsync(false);
        var gpt = Gpt("gpt-fail");
        var grok = Grok("grok-fail");
        _fixture.GptClient.FailNextWith("secret api key sk-test-123 prompt leaked");

        await chat.StartDiscussionAsync(new CommandSynapse<StartDiscussion>(
            Meta(Guid.NewGuid()),
            new StartDiscussion(
                "topic-fail",
                ((IAddressable)gpt).GetGrainId().Key.ToString()!,
                ((IAddressable)grok).GetGrainId().Key.ToString()!)));
        await chat.DrainOutboxAsync();
        await WaitForFailureAsync(chat, TimeSpan.FromSeconds(15));

        var diagnostics = await chat.GetDiagnosticsAsync();
        Assert.Equal("neuron failure", diagnostics.LastFailureMessage);
        Assert.DoesNotContain("sk-test", diagnostics.LastFailureMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("prompt", diagnostics.LastFailureMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        var surface = await chat.GetSurfaceAsync();
        var failureBlock = Assert.Single(surface.Surface.Blocks, block => block.Kind == "failure");
        Assert.Equal("neuron failure", failureBlock.Text);
        Assert.DoesNotContain("sk-test", failureBlock.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UiSurface_ids_are_unique_stable_and_derived_from_grain_key()
    {
        var chatA = Chat("surface-a");
        var chatB = Chat("surface-b");
        await chatA.SetAutoDrainAsync(false);
        await chatB.SetAutoDrainAsync(false);
        var gptA = Gpt("gpt-surface-a");
        var grokA = Grok("grok-surface-a");
        var gptB = Gpt("gpt-surface-b");
        var grokB = Grok("grok-surface-b");

        await chatA.StartDiscussionAsync(new CommandSynapse<StartDiscussion>(
            Meta(Guid.NewGuid()),
            new StartDiscussion("topic-a", ((IAddressable)gptA).GetGrainId().Key.ToString()!, ((IAddressable)grokA).GetGrainId().Key.ToString()!)));
        await chatB.StartDiscussionAsync(new CommandSynapse<StartDiscussion>(
            Meta(Guid.NewGuid()),
            new StartDiscussion("topic-b", ((IAddressable)gptB).GetGrainId().Key.ToString()!, ((IAddressable)grokB).GetGrainId().Key.ToString()!)));

        var surfaceA = await chatA.GetSurfaceAsync();
        var surfaceB = await chatB.GetSurfaceAsync();
        var keyA = ChatKey("surface-a");
        var keyB = ChatKey("surface-b");

        Assert.Equal(keyA, surfaceA.Surface.SurfaceId);
        Assert.Equal(keyB, surfaceB.Surface.SurfaceId);
        Assert.NotEqual(surfaceA.Surface.SurfaceId, surfaceB.Surface.SurfaceId);
        Assert.Equal(keyA, (await chatA.GetDiagnosticsAsync()).SurfaceId);

        var token = (await chatA.GetDiagnosticsAsync()).ActivationToken;
        var reloaded = await ReactivateAsync("surface-a", token);
        var restored = await reloaded.GetSurfaceAsync();
        Assert.Equal(keyA, restored.Surface.SurfaceId);
    }

    [Fact]
    public async Task Start_discussion_single_commit_is_coherent_after_reactivation_before_participant_step()
    {
        var chat = Chat("start-commit");
        await chat.SetAutoDrainAsync(false);
        var gpt = Gpt("gpt-start-commit");
        var grok = Grok("grok-start-commit");
        var gptKey = ((IAddressable)gpt).GetGrainId().Key.ToString()!;
        var grokKey = ((IAddressable)grok).GetGrainId().Key.ToString()!;

        var receipt = await chat.StartDiscussionAsync(new CommandSynapse<StartDiscussion>(
            Meta(Guid.NewGuid()),
            new StartDiscussion("topic-commit-coherent", gptKey, grokKey)));
        Assert.Equal(CommandReceiptStatus.Accepted, receipt.Status);

        var before = await chat.GetDiagnosticsAsync();
        Assert.Equal(0, before.StepCount);
        Assert.Equal(1, before.UiRevision);
        Assert.Equal(1, before.TranscriptCount);
        Assert.True(before.OutboxCount >= 1);
        Assert.Equal("topic-commit-coherent", before.Topic);
        Assert.Equal(gptKey, before.GptKey);
        Assert.Equal(grokKey, before.GrokKey);
        Assert.Equal("active", before.Status);
        Assert.Equal(0, _fixture.GptClient.InvocationCount);
        Assert.Equal(0, _fixture.GrokClient.InvocationCount);

        var reloaded = await ReactivateAsync("start-commit", before.ActivationToken);
        var after = await reloaded.GetDiagnosticsAsync();
        Assert.Equal(0, after.StepCount);
        Assert.Equal(1, after.UiRevision);
        Assert.Equal(1, after.TranscriptCount);
        Assert.True(after.OutboxCount >= 1);
        Assert.Equal("topic-commit-coherent", after.Topic);
        Assert.Equal(gptKey, after.GptKey);
        Assert.Equal(grokKey, after.GrokKey);
        Assert.Equal("active", after.Status);
        Assert.Equal(ChatKey("start-commit"), after.SurfaceId);
        Assert.Equal(0, _fixture.GptClient.InvocationCount);
        Assert.Equal(0, _fixture.GrokClient.InvocationCount);
        Assert.Null(after.CheckpointId);
        Assert.False(after.HasCheckpointJson);
    }

    [Fact]
    public async Task Checkpoint_json_persists_and_next_participant_runs_once_after_reactivation()
    {
        var chat = Chat("ckpt-durable");
        await chat.SetAutoDrainAsync(false);
        var gpt = Gpt("gpt-ckpt-durable");
        var grok = Grok("grok-ckpt-durable");

        await chat.StartDiscussionAsync(new CommandSynapse<StartDiscussion>(
            Meta(Guid.NewGuid()),
            new StartDiscussion(
                "topic-ckpt-durable",
                ((IAddressable)gpt).GetGrainId().Key.ToString()!,
                ((IAddressable)grok).GetGrainId().Key.ToString()!)));
        await chat.DrainOutboxAsync();
        await WaitForStepCountAsync(chat, 1, TimeSpan.FromSeconds(15));

        var before = await chat.GetDiagnosticsAsync();
        Assert.True(before.HasCheckpointJson);
        Assert.True(before.CheckpointJsonLength > 2);
        Assert.False(string.IsNullOrWhiteSpace(before.CheckpointId));
        Assert.Equal(1, before.ParticipantCursor);

        var reloaded = await ReactivateAsync("ckpt-durable", before.ActivationToken);
        var restored = await reloaded.GetDiagnosticsAsync();
        Assert.True(restored.HasCheckpointJson);
        Assert.Equal(before.CheckpointJsonLength, restored.CheckpointJsonLength);
        Assert.Equal(before.CheckpointId, restored.CheckpointId);
        Assert.Equal(before.CheckpointSessionId, restored.CheckpointSessionId);

        var grokBefore = _fixture.GrokClient.InvocationCount;
        await reloaded.SetAutoDrainAsync(false);
        await reloaded.DrainOutboxAsync();
        await WaitForStepCountAsync(reloaded, 2, TimeSpan.FromSeconds(15));
        Assert.Equal(grokBefore + 1, _fixture.GrokClient.InvocationCount);
    }

    [Fact]
    public async Task Provider_timeout_cancels_once_and_is_sanitized_without_retry()
    {
        var gpt = Gpt("gpt-timeout");
        _fixture.GptClient.HangUntilCancelled();

        var ex = await Assert.ThrowsAsync<BrainException>(() =>
            gpt.CompleteTurnAsync(new CommandSynapse<AgentTurnRequest>(
                Meta(Guid.NewGuid()),
                new AgentTurnRequest("req-timeout", "hang-please"))));

        Assert.Equal(BrainErrors.FailureSanitized, ex.Code);
        Assert.Equal(1, _fixture.GptClient.InvocationCount);
        Assert.Equal(1, _fixture.GptClient.CancellationObservedCount);

        _fixture.GptClient.Reset();
        _fixture.GptClient.HangUntilCancelled();
        await Assert.ThrowsAsync<BrainException>(() =>
            gpt.CompleteTurnAsync(new CommandSynapse<AgentTurnRequest>(
                Meta(Guid.NewGuid()),
                new AgentTurnRequest("req-timeout-2", "hang-again"))));
        Assert.Equal(1, _fixture.GptClient.InvocationCount);
        Assert.Equal(1, _fixture.GptClient.CancellationObservedCount);
    }

    [Fact]
    public async Task No_prompt_token_or_provider_payload_appears_in_telemetry()
    {
        const string secretPrompt = "SECRET_PROMPT_sk-live-telemetry-leak-999";
        const string secretToken = "tok_live_abcDEF123leak";
        var captured = new List<string>();

        using var listener = new ActivityListener
        {
            ShouldListenTo = static _ => true,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                captured.Add(activity.DisplayName ?? string.Empty);
                captured.Add(activity.OperationName ?? string.Empty);
                if (activity.StatusDescription is not null)
                    captured.Add(activity.StatusDescription);
                foreach (var tag in activity.Tags)
                {
                    captured.Add(tag.Key);
                    captured.Add(tag.Value ?? string.Empty);
                }

                foreach (var tag in activity.TagObjects)
                {
                    captured.Add(tag.Key);
                    captured.Add(tag.Value?.ToString() ?? string.Empty);
                }

                foreach (var baggage in activity.Baggage)
                {
                    captured.Add(baggage.Key);
                    captured.Add(baggage.Value ?? string.Empty);
                }

                foreach (var activityEvent in activity.Events)
                {
                    captured.Add(activityEvent.Name);
                    foreach (var tag in activityEvent.Tags)
                    {
                        captured.Add(tag.Key);
                        captured.Add(tag.Value?.ToString() ?? string.Empty);
                    }
                }
            }
        };
        ActivitySource.AddActivityListener(listener);

        var chat = Chat("telemetry-fail");
        await chat.SetAutoDrainAsync(false);
        var gpt = Gpt("gpt-telemetry");
        var grok = Grok("grok-telemetry");
        _fixture.GptClient.FailNextWith($"{secretPrompt} {secretToken}");

        await chat.StartDiscussionAsync(new CommandSynapse<StartDiscussion>(
            Meta(Guid.NewGuid()),
            new StartDiscussion(
                secretPrompt,
                ((IAddressable)gpt).GetGrainId().Key.ToString()!,
                ((IAddressable)grok).GetGrainId().Key.ToString()!)));
        await chat.DrainOutboxAsync();
        await WaitForFailureAsync(chat, TimeSpan.FromSeconds(15));

        var surface = await chat.GetSurfaceAsync();
        var failure = Assert.Single(surface.Surface.Blocks, block => block.Kind == "failure");
        Assert.Equal("neuron failure", failure.Text);
        Assert.DoesNotContain(secretPrompt, failure.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(secretToken, failure.Text, StringComparison.Ordinal);

        foreach (var fragment in captured)
        {
            Assert.DoesNotContain(secretPrompt, fragment, StringComparison.Ordinal);
            Assert.DoesNotContain(secretToken, fragment, StringComparison.Ordinal);
            Assert.DoesNotContain("sk-live", fragment, StringComparison.OrdinalIgnoreCase);
        }

        var aiDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Brain.AI"));
        Assert.True(Directory.Exists(aiDir), aiDir);
        foreach (var file in Directory.EnumerateFiles(aiDir, "*.cs"))
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("SetTag(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("AddTag(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("token_usage", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("completion_tokens", source, StringComparison.OrdinalIgnoreCase);
        }
    }
}
