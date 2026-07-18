using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;
using Microsoft.Extensions.AI;
using Xunit;

namespace Brain.FeasibilityTests.AgentFramework;

public sealed class GroupChatOneStepKillGateTests
{
    [Fact]
    public async Task One_advance_produces_one_participant_response()
    {
        var harness = GroupChatOneStepHarness.Create();
        var result = await harness.AdvanceOnceAsync();

        Assert.Single(result.ParticipantResponses);
        Assert.Equal("alpha-reply", result.ParticipantResponses[0].Text);
        Assert.Equal(1, harness.AlphaClient.InvocationCount);
        Assert.Equal(0, harness.BetaClient.InvocationCount);
    }

    [Fact]
    public async Task One_advance_produces_one_checkpoint()
    {
        var harness = GroupChatOneStepHarness.Create();
        var result = await harness.AdvanceOnceAsync();

        Assert.NotNull(result.Checkpoint);
        Assert.False(string.IsNullOrWhiteSpace(result.Checkpoint.CheckpointId));
        Assert.False(string.IsNullOrWhiteSpace(result.Checkpoint.SessionId));
        Assert.True(result.SuperStepCompletions >= 1);
        Assert.NotEmpty(await harness.Store.RetrieveIndexAsync(result.Checkpoint.SessionId));
        var payload = await harness.Store.RetrieveCheckpointAsync(result.Checkpoint.SessionId, result.Checkpoint);
        Assert.Equal(JsonValueKind.Object, payload.ValueKind);
    }

    [Fact]
    public async Task Second_advance_selects_the_next_participant_once()
    {
        var harness = GroupChatOneStepHarness.Create();
        var first = await harness.AdvanceOnceAsync();
        Assert.Single(first.ParticipantResponses);
        Assert.Equal("alpha-reply", first.ParticipantResponses[0].Text);

        var second = await harness.AdvanceNextParticipantAsync(first.Checkpoint);
        Assert.Single(second.ParticipantResponses);
        Assert.Equal("beta-reply", second.ParticipantResponses[0].Text);
        Assert.Equal(0, harness.AlphaClient.InvocationCount);
        Assert.Equal(1, harness.BetaClient.InvocationCount);
        Assert.True(second.UsedCheckpointResume || second.UsedRebuildFallback);
    }

    [Fact]
    public async Task Stopping_after_the_checkpoint_leaves_no_background_conversation()
    {
        var harness = GroupChatOneStepHarness.Create();
        var alphaBefore = harness.AlphaClient.TotalInvocationCount;
        var betaBefore = harness.BetaClient.TotalInvocationCount;

        var result = await harness.AdvanceOnceAsync();
        Assert.Single(result.ParticipantResponses);
        Assert.NotNull(result.Checkpoint);

        var alphaAtStop = harness.AlphaClient.TotalInvocationCount;
        var betaAtStop = harness.BetaClient.TotalInvocationCount;
        Assert.Equal(alphaBefore + 1, alphaAtStop);
        Assert.Equal(betaBefore, betaAtStop);

        await Task.Delay(250);

        Assert.Equal(alphaAtStop, harness.AlphaClient.TotalInvocationCount);
        Assert.Equal(betaAtStop, harness.BetaClient.TotalInvocationCount);
        Assert.True(
            result.StatusAtStop is RunStatus.Idle or RunStatus.Ended or RunStatus.PendingRequests,
            $"Unexpected run status after stop: {result.StatusAtStop}");
        Assert.Equal(RunStatus.Ended, result.StatusAfterDispose);
    }

    [Fact]
    public async Task Restored_or_rebuilt_state_preserves_transcript_and_participant_cursor()
    {
        var harness = GroupChatOneStepHarness.Create();
        var first = await harness.AdvanceOnceAsync();
        Assert.Single(first.ParticipantResponses);
        Assert.NotNull(first.Checkpoint);

        var transcriptAfterFirst = harness.Transcript
            .Select(message => $"{message.Role}:{message.Text}")
            .ToArray();
        Assert.Contains(transcriptAfterFirst, line => line.Contains("alpha-reply", StringComparison.Ordinal));
        Assert.Equal(1, harness.ParticipantCursor);

        var second = await harness.AdvanceNextParticipantAsync(first.Checkpoint);
        Assert.Single(second.ParticipantResponses);
        Assert.Equal("beta-reply", second.ParticipantResponses[0].Text);

        var transcriptAfterSecond = harness.Transcript
            .Select(message => $"{message.Role}:{message.Text}")
            .ToArray();
        Assert.Contains(transcriptAfterSecond, line => line.Contains("alpha-reply", StringComparison.Ordinal));
        Assert.Contains(transcriptAfterSecond, line => line.Contains("beta-reply", StringComparison.Ordinal));
        Assert.Equal(0, harness.ParticipantCursor);
        Assert.True(second.UsedCheckpointResume || second.UsedRebuildFallback);
    }
}

internal sealed class GroupChatOneStepHarness
{
    private GroupChatOneStepHarness(
        ScriptedChatClient alphaClient,
        ScriptedChatClient betaClient,
        AIAgent alpha,
        AIAgent beta,
        InMemoryJsonCheckpointStore store,
        CheckpointManager checkpointManager)
    {
        AlphaClient = alphaClient;
        BetaClient = betaClient;
        Alpha = alpha;
        Beta = beta;
        Store = store;
        CheckpointManager = checkpointManager;
        Transcript = [new ChatMessage(ChatRole.User, "seed topic")];
        ParticipantCursor = 0;
    }

    public ScriptedChatClient AlphaClient { get; }
    public ScriptedChatClient BetaClient { get; }
    public AIAgent Alpha { get; }
    public AIAgent Beta { get; }
    public InMemoryJsonCheckpointStore Store { get; }
    public CheckpointManager CheckpointManager { get; }
    public List<ChatMessage> Transcript { get; }
    public int ParticipantCursor { get; private set; }
    public CheckpointInfo? LastCheckpoint { get; private set; }

    public static GroupChatOneStepHarness Create()
    {
        var alphaClient = new ScriptedChatClient("alpha-reply");
        var betaClient = new ScriptedChatClient("beta-reply");
        var alpha = new ChatClientAgent(alphaClient, name: "Alpha");
        var beta = new ChatClientAgent(betaClient, name: "Beta");
        var store = new InMemoryJsonCheckpointStore();
        var checkpointManager = CheckpointManager.CreateJson(store);
        return new GroupChatOneStepHarness(alphaClient, betaClient, alpha, beta, store, checkpointManager);
    }

    public Task<AdvanceResult> AdvanceOnceAsync() =>
        ExecuteAdvanceAsync(rebuild: false);

    public async Task<AdvanceResult> AdvanceNextParticipantAsync(CheckpointInfo? capturedCheckpoint)
    {
        if (capturedCheckpoint is not null)
        {
            var resumeAttempt = await TryResumeAdvanceAsync(capturedCheckpoint).ConfigureAwait(false);
            if (resumeAttempt is { ParticipantResponses.Count: 1 })
            {
                return resumeAttempt with { UsedCheckpointResume = true };
            }
        }

        var rebuilt = await ExecuteAdvanceAsync(rebuild: true).ConfigureAwait(false);
        return rebuilt with { UsedRebuildFallback = true };
    }

    private async Task<AdvanceResult?> TryResumeAdvanceAsync(CheckpointInfo capturedCheckpoint)
    {
        AlphaClient.ResetInvocationCount();
        BetaClient.ResetInvocationCount();

        var workflow = BuildWorkflow(maximumIterationCount: 2);
        var environment = InProcessExecution.Lockstep.WithCheckpointing(CheckpointManager);
        await using StreamingRun run = await environment
            .ResumeStreamingAsync(workflow, capturedCheckpoint)
            .ConfigureAwait(false);

        var turnAccepted = await run.TrySendMessageAsync(new TurnToken(emitEvents: true)).ConfigureAwait(false);
        if (!turnAccepted)
        {
            return null;
        }

        var drained = await DrainOneParticipantStepAsync(run).ConfigureAwait(false);
        var advancedNextParticipant =
            drained.ParticipantResponses.Count == 1 &&
            BetaClient.InvocationCount == 1 &&
            AlphaClient.InvocationCount == 0 &&
            string.Equals(drained.ParticipantResponses[0].Text, "beta-reply", StringComparison.Ordinal);

        if (!advancedNextParticipant)
        {
            return null;
        }

        MergeTranscript(drained.TerminalTranscript, drained.ParticipantResponses);
        ParticipantCursor = (ParticipantCursor + 1) % 2;

        return new AdvanceResult(
            drained.ParticipantResponses,
            drained.Checkpoint ?? LastCheckpoint,
            drained.SuperStepCompletions,
            drained.StatusAtStop,
            RunStatus.Ended,
            UsedCheckpointResume: true,
            UsedRebuildFallback: false);
    }

    private async Task<AdvanceResult> ExecuteAdvanceAsync(bool rebuild)
    {
        AlphaClient.ResetInvocationCount();
        BetaClient.ResetInvocationCount();

        var workflow = rebuild
            ? BuildRebuiltWorkflow()
            : BuildWorkflow(maximumIterationCount: 1);
        var environment = InProcessExecution.Lockstep.WithCheckpointing(CheckpointManager);

        await using StreamingRun run = await environment
            .RunStreamingAsync(workflow, CloneTranscript())
            .ConfigureAwait(false);

        await run.TrySendMessageAsync(new TurnToken(emitEvents: true)).ConfigureAwait(false);

        var drained = await DrainOneParticipantStepAsync(run).ConfigureAwait(false);

        MergeTranscript(drained.TerminalTranscript, drained.ParticipantResponses);

        if (drained.ParticipantResponses.Count > 0)
        {
            ParticipantCursor = (ParticipantCursor + 1) % 2;
        }

        return new AdvanceResult(
            drained.ParticipantResponses,
            drained.Checkpoint ?? LastCheckpoint,
            drained.SuperStepCompletions,
            drained.StatusAtStop,
            RunStatus.Ended,
            UsedCheckpointResume: false,
            UsedRebuildFallback: false);
    }

    private async Task<DrainedStep> DrainOneParticipantStepAsync(StreamingRun run)
    {
        var participantResponses = new List<ChatMessage>();
        CheckpointInfo? stepCheckpoint = null;
        List<ChatMessage>? terminalTranscript = null;
        var superStepCompletions = 0;

        await foreach (WorkflowEvent workflowEvent in run.WatchStreamAsync().ConfigureAwait(false))
        {
            switch (workflowEvent)
            {
                case AgentResponseEvent agentResponse:
                    foreach (var message in agentResponse.Response.Messages)
                    {
                        if (!string.IsNullOrWhiteSpace(message.Text))
                        {
                            participantResponses.Add(message);
                        }
                    }
                    break;

                case AgentResponseUpdateEvent update:
                    var streamed = update.AsResponse();
                    foreach (var message in streamed.Messages)
                    {
                        if (!string.IsNullOrWhiteSpace(message.Text))
                        {
                            participantResponses.Add(message);
                        }
                    }
                    break;

                case SuperStepCompletedEvent superStepCompleted:
                    superStepCompletions++;
                    if (superStepCompleted.CompletionInfo?.Checkpoint is { } checkpoint)
                    {
                        stepCheckpoint = checkpoint;
                        LastCheckpoint = checkpoint;
                    }
                    break;

                case WorkflowOutputEvent output when output.Is<List<ChatMessage>>():
                    terminalTranscript = output.As<List<ChatMessage>>();
                    break;
            }

            if (DeduplicateResponses(participantResponses).Count >= 1 && stepCheckpoint is not null)
            {
                break;
            }
        }

        await run.CancelRunAsync().ConfigureAwait(false);
        var statusAtStop = await run.GetStatusAsync().ConfigureAwait(false);

        return new DrainedStep(
            DeduplicateResponses(participantResponses),
            stepCheckpoint,
            superStepCompletions,
            statusAtStop,
            terminalTranscript);
    }

    private sealed record DrainedStep(
        IReadOnlyList<ChatMessage> ParticipantResponses,
        CheckpointInfo? Checkpoint,
        int SuperStepCompletions,
        RunStatus StatusAtStop,
        List<ChatMessage>? TerminalTranscript);

    private Workflow BuildWorkflow(int maximumIterationCount) =>
        AgentWorkflowBuilder
            .CreateGroupChatBuilderWith(agents =>
                new RoundRobinGroupChatManager(agents)
                {
                    MaximumIterationCount = maximumIterationCount
                })
            .AddParticipants(Alpha, Beta)
            .Build();

    private Workflow BuildRebuiltWorkflow()
    {
        AIAgent[] ordered = ParticipantCursor == 0
            ? [Alpha, Beta]
            : [Beta, Alpha];

        return AgentWorkflowBuilder
            .CreateGroupChatBuilderWith(agents =>
                new RoundRobinGroupChatManager(agents)
                {
                    MaximumIterationCount = 1
                })
            .AddParticipants(ordered)
            .Build();
    }

    private List<ChatMessage> CloneTranscript() =>
        Transcript.Select(message => new ChatMessage(message.Role, message.Text)
        {
            AuthorName = message.AuthorName
        }).ToList();

    private void MergeTranscript(List<ChatMessage>? terminalTranscript, IReadOnlyList<ChatMessage> participantResponses)
    {
        if (terminalTranscript is { Count: > 0 })
        {
            Transcript.Clear();
            Transcript.AddRange(terminalTranscript);
            return;
        }

        foreach (var message in DeduplicateResponses(participantResponses))
        {
            if (Transcript.Any(existing => existing.Text == message.Text && existing.Role == message.Role))
            {
                continue;
            }

            Transcript.Add(message);
        }
    }

    private static List<ChatMessage> DeduplicateResponses(IReadOnlyList<ChatMessage> responses)
    {
        var deduped = new List<ChatMessage>();
        foreach (var message in responses)
        {
            if (deduped.Any(existing => existing.Text == message.Text && existing.AuthorName == message.AuthorName))
            {
                continue;
            }

            deduped.Add(message);
        }

        return deduped;
    }
}

internal sealed record AdvanceResult(
    IReadOnlyList<ChatMessage> ParticipantResponses,
    CheckpointInfo? Checkpoint,
    int SuperStepCompletions,
    RunStatus StatusAtStop,
    RunStatus StatusAfterDispose,
    bool UsedCheckpointResume,
    bool UsedRebuildFallback);

internal sealed class ScriptedChatClient(string replyText) : IChatClient
{
    private int _invocationCount;
    private int _totalInvocationCount;

    public int InvocationCount => Volatile.Read(ref _invocationCount);
    public int TotalInvocationCount => Volatile.Read(ref _totalInvocationCount);

    public void ResetInvocationCount() => Volatile.Write(ref _invocationCount, 0);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _invocationCount);
        Interlocked.Increment(ref _totalInvocationCount);
        var message = new ChatMessage(ChatRole.Assistant, replyText);
        return Task.FromResult(new ChatResponse(message));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _invocationCount);
        Interlocked.Increment(ref _totalInvocationCount);
        yield return new ChatResponseUpdate(ChatRole.Assistant, replyText);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

internal sealed class InMemoryJsonCheckpointStore : ICheckpointStore<JsonElement>
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, JsonElement>> _sessions = new();

    public ValueTask<CheckpointInfo> CreateCheckpointAsync(
        string sessionId,
        JsonElement value,
        CheckpointInfo? parent = null)
    {
        var checkpoint = new CheckpointInfo(sessionId, Guid.NewGuid().ToString("N"));
        var bag = _sessions.GetOrAdd(sessionId, static _ => new ConcurrentDictionary<string, JsonElement>());
        bag[checkpoint.CheckpointId] = value.Clone();
        return ValueTask.FromResult(checkpoint);
    }

    public ValueTask<JsonElement> RetrieveCheckpointAsync(string sessionId, CheckpointInfo key)
    {
        if (!_sessions.TryGetValue(sessionId, out var bag) ||
            !bag.TryGetValue(key.CheckpointId, out var value))
        {
            throw new KeyNotFoundException($"Checkpoint {key.CheckpointId} was not found for session {sessionId}.");
        }

        return ValueTask.FromResult(value.Clone());
    }

    public ValueTask<IEnumerable<CheckpointInfo>> RetrieveIndexAsync(
        string sessionId,
        CheckpointInfo? withParent = null)
    {
        if (!_sessions.TryGetValue(sessionId, out var bag))
        {
            return ValueTask.FromResult(Enumerable.Empty<CheckpointInfo>());
        }

        IEnumerable<CheckpointInfo> index = bag.Keys
            .Select(checkpointId => new CheckpointInfo(sessionId, checkpointId))
            .ToArray();
        return ValueTask.FromResult(index);
    }
}
