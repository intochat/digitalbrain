namespace DigitalBrain.AI;

using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;
using Microsoft.Extensions.AI;

public sealed class OneStepGroupChatEngine
{
    public async Task<OneStepResult> AdvanceAsync(
        IReadOnlyList<ChatMessage> transcript,
        int participantCursor,
        AIAgent first,
        AIAgent second,
        string? checkpointSessionId,
        string? checkpointId,
        string? checkpointJson)
    {
        var ordered = participantCursor == 0
            ? new[] { first, second }
            : new[] { second, first };

        if (!string.IsNullOrWhiteSpace(checkpointSessionId)
            && !string.IsNullOrWhiteSpace(checkpointId)
            && !string.IsNullOrWhiteSpace(checkpointJson))
        {
            var resumeStore = new BufferedJsonCheckpointStore();
            resumeStore.Seed(
                checkpointSessionId,
                checkpointId,
                JsonSerializer.Deserialize<JsonElement>(checkpointJson));
            var resumeManager = CheckpointManager.CreateJson(resumeStore);
            var resume = await TryResumeOrNullAsync(
                ordered,
                resumeManager,
                resumeStore,
                new CheckpointInfo(checkpointSessionId, checkpointId)).ConfigureAwait(false);
            if (resume is not null)
                return resume;
        }

        var freshStore = new BufferedJsonCheckpointStore();
        var freshManager = CheckpointManager.CreateJson(freshStore);
        return await ExecuteFreshAsync(transcript, ordered, freshManager, freshStore).ConfigureAwait(false);
    }

    private async Task<OneStepResult?> TryResumeOrNullAsync(
        AIAgent[] ordered,
        CheckpointManager checkpointManager,
        BufferedJsonCheckpointStore store,
        CheckpointInfo checkpoint)
    {
        try
        {
            var workflow = BuildWorkflow(ordered, maximumIterationCount: 2);
            var environment = InProcessExecution.Lockstep.WithCheckpointing(checkpointManager);
            await using StreamingRun run = await environment
                .ResumeStreamingAsync(workflow, checkpoint)
                .ConfigureAwait(false);

            var accepted = await run.TrySendMessageAsync(new TurnToken(emitEvents: true)).ConfigureAwait(false);
            if (!accepted)
                return null;

            var drained = await DrainOneParticipantStepAsync(run).ConfigureAwait(false);
            if (drained.ParticipantResponses.Count != 1 || drained.Checkpoint is null)
                return null;

            var json = store.TryGetJson(drained.Checkpoint.SessionId, drained.Checkpoint.CheckpointId);
            return new OneStepResult(
                drained.ParticipantResponses,
                drained.Checkpoint,
                json,
                drained.TerminalTranscript,
                UsedCheckpointResume: true);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<OneStepResult> ExecuteFreshAsync(
        IReadOnlyList<ChatMessage> transcript,
        AIAgent[] ordered,
        CheckpointManager checkpointManager,
        BufferedJsonCheckpointStore store)
    {
        var workflow = BuildWorkflow(ordered, maximumIterationCount: 1);
        var environment = InProcessExecution.Lockstep.WithCheckpointing(checkpointManager);
        var input = transcript.Select(CloneMessage).ToList();

        await using StreamingRun run = await environment
            .RunStreamingAsync(workflow, input)
            .ConfigureAwait(false);

        await run.TrySendMessageAsync(new TurnToken(emitEvents: true)).ConfigureAwait(false);
        var drained = await DrainOneParticipantStepAsync(run).ConfigureAwait(false);

        if (drained.ParticipantResponses.Count != 1)
        {
            throw new Brain.Contracts.BrainException(
                Brain.Contracts.BrainErrors.FailureSanitized,
                "neuron failure");
        }

        if (drained.Checkpoint is null)
        {
            throw new Brain.Contracts.BrainException(
                Brain.Contracts.BrainErrors.FailureSanitized,
                "neuron failure");
        }

        var json = store.TryGetJson(drained.Checkpoint.SessionId, drained.Checkpoint.CheckpointId);
        return new OneStepResult(
            drained.ParticipantResponses,
            drained.Checkpoint,
            json,
            drained.TerminalTranscript,
            UsedCheckpointResume: false);
    }

    private static Workflow BuildWorkflow(AIAgent[] ordered, int maximumIterationCount) =>
        AgentWorkflowBuilder
            .CreateGroupChatBuilderWith(agents =>
                new RoundRobinGroupChatManager(agents)
                {
                    MaximumIterationCount = maximumIterationCount
                })
            .AddParticipants(ordered)
            .Build();

    private static async Task<DrainedStep> DrainOneParticipantStepAsync(StreamingRun run)
    {
        var participantResponses = new List<ChatMessage>();
        CheckpointInfo? selected = null;
        List<ChatMessage>? terminalTranscript = null;

        await foreach (WorkflowEvent workflowEvent in run.WatchStreamAsync().ConfigureAwait(false))
        {
            switch (workflowEvent)
            {
                case AgentResponseEvent agentResponse:
                    foreach (var message in agentResponse.Response.Messages)
                    {
                        if (!string.IsNullOrWhiteSpace(message.Text))
                            participantResponses.Add(message);
                    }
                    break;

                case AgentResponseUpdateEvent update:
                    var streamed = update.AsResponse();
                    foreach (var message in streamed.Messages)
                    {
                        if (!string.IsNullOrWhiteSpace(message.Text))
                            participantResponses.Add(message);
                    }
                    break;

                case SuperStepCompletedEvent superStepCompleted:
                    if (superStepCompleted.CompletionInfo?.Checkpoint is { } checkpoint
                        && Deduplicate(participantResponses).Count >= 1)
                    {
                        selected = checkpoint;
                    }
                    break;

                case WorkflowOutputEvent output when output.Is<List<ChatMessage>>() && !output.IsIntermediate():
                    terminalTranscript = output.As<List<ChatMessage>>();
                    break;
            }

            if (Deduplicate(participantResponses).Count >= 1 && selected is not null)
                break;
        }

        await run.CancelRunAsync().ConfigureAwait(false);
        return new DrainedStep(Deduplicate(participantResponses), selected, terminalTranscript);
    }

    private static List<ChatMessage> Deduplicate(IReadOnlyList<ChatMessage> responses)
    {
        var deduped = new List<ChatMessage>();
        foreach (var message in responses)
        {
            if (deduped.Any(existing =>
                    existing.Text == message.Text
                    && existing.AuthorName == message.AuthorName
                    && existing.Role == message.Role))
            {
                continue;
            }

            deduped.Add(message);
        }

        return deduped;
    }

    private static ChatMessage CloneMessage(ChatMessage message) =>
        new(message.Role, message.Text)
        {
            AuthorName = message.AuthorName
        };

    private sealed record DrainedStep(
        IReadOnlyList<ChatMessage> ParticipantResponses,
        CheckpointInfo? Checkpoint,
        List<ChatMessage>? TerminalTranscript);
}

public sealed record OneStepResult(
    IReadOnlyList<ChatMessage> ParticipantResponses,
    CheckpointInfo Checkpoint,
    string? CheckpointJson,
    List<ChatMessage>? TerminalTranscript,
    bool UsedCheckpointResume);

internal sealed class BufferedJsonCheckpointStore : ICheckpointStore<JsonElement>
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, JsonElement>> _sessions = new();

    public void Seed(string sessionId, string checkpointId, JsonElement value)
    {
        var bag = _sessions.GetOrAdd(sessionId, static _ => new ConcurrentDictionary<string, JsonElement>());
        bag[checkpointId] = value.Clone();
    }

    public bool Has(string sessionId, string checkpointId) =>
        _sessions.TryGetValue(sessionId, out var bag) && bag.ContainsKey(checkpointId);

    public string? TryGetJson(string sessionId, string checkpointId)
    {
        if (!_sessions.TryGetValue(sessionId, out var bag) || !bag.TryGetValue(checkpointId, out var value))
            return null;
        return value.GetRawText();
    }

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
        if (!_sessions.TryGetValue(sessionId, out var bag) || !bag.TryGetValue(key.CheckpointId, out var value))
            throw new KeyNotFoundException($"Checkpoint {key.CheckpointId} was not found for session {sessionId}.");
        return ValueTask.FromResult(value.Clone());
    }

    public ValueTask<IEnumerable<CheckpointInfo>> RetrieveIndexAsync(
        string sessionId,
        CheckpointInfo? withParent = null)
    {
        if (!_sessions.TryGetValue(sessionId, out var bag))
            return ValueTask.FromResult(Enumerable.Empty<CheckpointInfo>());

        IEnumerable<CheckpointInfo> index = bag.Keys
            .Select(checkpointId => new CheckpointInfo(sessionId, checkpointId))
            .ToArray();
        return ValueTask.FromResult(index);
    }
}
