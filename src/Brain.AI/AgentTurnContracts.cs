namespace DigitalBrain.AI;

using Brain.Contracts;

[GenerateSerializer, Alias("digitalbrain.ai.agent-turn-request.v1")]
public sealed record AgentTurnRequest(
    [property: Id(0)] string RequestId,
    [property: Id(1)] string InputText);

[GenerateSerializer, Alias("digitalbrain.ai.agent-turn-result.v1")]
public sealed record AgentTurnResult(
    [property: Id(0)] string RequestId,
    [property: Id(1)] string ResponseText,
    [property: Id(2)] long Revision);

[GenerateSerializer, Alias("digitalbrain.ai.agent-turn-state.v1")]
public sealed record AgentTurnStateSnapshot(
    [property: Id(0)] string Identity,
    [property: Id(1)] string? LastRequestId,
    [property: Id(2)] int CompletedTurnCount,
    [property: Id(3)] long Revision);

[Alias("digitalbrain.ai.IGpt56Turn")]
public interface IGpt56Turn : IGpt56
{
    [Alias("CompleteTurnAsync")]
    Task<AgentTurnResult> CompleteTurnAsync(CommandSynapse<AgentTurnRequest> command);

    [Alias("GetTurnStateAsync")]
    Task<AgentTurnStateSnapshot> GetTurnStateAsync();
}

[Alias("digitalbrain.ai.IGrok45Turn")]
public interface IGrok45Turn : IGrok45
{
    [Alias("CompleteTurnAsync")]
    Task<AgentTurnResult> CompleteTurnAsync(CommandSynapse<AgentTurnRequest> command);

    [Alias("GetTurnStateAsync")]
    Task<AgentTurnStateSnapshot> GetTurnStateAsync();
}

[GenerateSerializer, Alias("digitalbrain.ai.group-chat-step.v1")]
public sealed record GroupChatStepEvent(
    [property: Id(0)] int StepIndex,
    [property: Id(1)] Guid DiscussionId,
    [property: Id(2)] string IntentKind = "step",
    [property: Id(3)] UiFeedCandidate? Candidate = null)
{
    public const string StepKind = "step";
    public const string UiKind = "ui";

    public bool IsUiIntent =>
        string.Equals(IntentKind, UiKind, StringComparison.Ordinal)
        && Candidate is not null;

    public bool IsStepIntent =>
        string.Equals(IntentKind, StepKind, StringComparison.Ordinal)
        && Candidate is null;
}

[GenerateSerializer, Alias("digitalbrain.ai.group-chat-diagnostics.v1")]
public sealed record GroupChatDiagnosticsSnapshot(
    [property: Id(0)] int TranscriptCount,
    [property: Id(1)] int ParticipantCursor,
    [property: Id(2)] int StepCount,
    [property: Id(3)] string? CheckpointId,
    [property: Id(4)] string? CheckpointSessionId,
    [property: Id(5)] bool IsCancelled,
    [property: Id(6)] int OutboxCount,
    [property: Id(7)] long UiRevision,
    [property: Id(8)] long Revision,
    [property: Id(9)] Guid ActivationToken,
    [property: Id(10)] IReadOnlyList<string> TranscriptTexts,
    [property: Id(11)] string? LastFailureMessage,
    [property: Id(12)] bool HasCheckpointJson,
    [property: Id(13)] int CheckpointJsonLength,
    [property: Id(14)] string SurfaceId,
    [property: Id(15)] string? Topic,
    [property: Id(16)] string? GptKey,
    [property: Id(17)] string? GrokKey,
    [property: Id(18)] string Status);

[Alias("digitalbrain.ai.IGroupChatControl")]
public interface IGroupChatControl : IGroupChat
{
    [Alias("GetDiagnosticsAsync")]
    Task<GroupChatDiagnosticsSnapshot> GetDiagnosticsAsync();

    [Alias("SetAutoDrainAsync")]
    Task SetAutoDrainAsync(bool enabled);

    [Alias("DrainOutboxAsync")]
    Task DrainOutboxAsync();

    [Alias("RequestDeactivationAsync")]
    Task RequestDeactivationAsync();

    [Alias("PeekOutboxEventAsync")]
    Task<EventSynapse<GroupChatStepEvent>?> PeekOutboxEventAsync();

    [Alias("PeekStepOutboxEventAsync")]
    Task<EventSynapse<GroupChatStepEvent>?> PeekStepOutboxEventAsync();

    [Alias("PublishStepEventAsync")]
    Task PublishStepEventAsync(EventSynapse<GroupChatStepEvent> @event);

}
