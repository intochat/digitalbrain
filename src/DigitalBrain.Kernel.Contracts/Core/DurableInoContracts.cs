using System.Text.Json;
using DigitalBrain.Kernel.Contracts;
using Orleans;
namespace DigitalBrain.Kernel.Contracts.Runtime;

public enum InoOperationPhase
{
    Accepted = 0,
    Queued = 1,
    Running = 2,
    AwaitingAuthorization = 3,
    RetryScheduled = 4,
    Succeeded = 5,
    Failed = 6,
    OutcomeUnknown = 7,
    Cancelled = 8,
    AwaitingApproval = 9,
    Approved = 10,
    ApplyingEffect = 11
}
[GenerateSerializer, Alias("digitalbrain.runtime.accepted-command")]
public sealed record AcceptedCommand(
    [property: Id(0)] string CommandId,
    [property: Id(1)] string OperationId,
    [property: Id(2)] string ConversationId,
    [property: Id(3)] string ActorScope,
    [property: Id(4)] string IdempotencyKey,
    [property: Id(5)] string InputHash,
    [property: Id(6)] string RequestId,
    [property: Id(7)] DateTimeOffset AcceptedAt,
    [property: Id(8)] int SchemaVersion);
[GenerateSerializer, Alias("digitalbrain.runtime.operation-receipt")]
public sealed record OperationReceipt(
    [property: Id(0)] string OperationId,
    [property: Id(1)] string IdempotencyKey,
    [property: Id(2)] InoOperationPhase Phase,
    [property: Id(3)] long Version);
[GenerateSerializer, Alias("digitalbrain.runtime.approval-record")]
public sealed record ApprovalRecord(
    [property: Id(0)] string ApprovalId,
    [property: Id(1)] string OperationId,
    [property: Id(2)] string EffectId,
    [property: Id(3)] string State,
    [property: Id(4)] long Version,
    [property: Id(5)] DateTimeOffset RequestedAt,
    [property: Id(6)] DateTimeOffset? DecidedAt = null,
    [property: Id(7)] string? DecidedBy = null,
    [property: Id(8)] string? DecisionId = null);
[GenerateSerializer, Alias("digitalbrain.runtime.effect-record")]
public sealed record EffectRecord(
    [property: Id(0)] string EffectId,
    [property: Id(1)] string OperationId,
    [property: Id(2)] string Kind,
    [property: Id(3)] string Scope,
    [property: Id(4)] string State,
    [property: Id(5)] string ProviderIdempotencyKey,
    [property: Id(6)] long Version);
[GenerateSerializer, Alias("digitalbrain.runtime.operation-feed-turn")]
public sealed record OperationFeedTurn([property: Id(0)] string CommandId, [property: Id(1)] string Role, [property: Id(2)] string Text, [property: Id(3)] string State);
[GenerateSerializer, Alias("digitalbrain.runtime.operation-feed-view")]
public sealed record OperationFeedView(
    [property: Id(0)] string CommandId,
    [property: Id(1)] string State,
    [property: Id(2)] bool Retryable,
    [property: Id(3)] string? SafeReason,
    [property: Id(4)] string? ApprovalId,
    [property: Id(5)] ToolAction? Action,
    [property: Id(6)] OperationFeedTurn[] Turns);
[GenerateSerializer, Alias("digitalbrain.runtime.operation-outbox-record")]
public sealed record OperationOutboxRecord(
    [property: Id(0)] string EventId,
    [property: Id(1)] string OperationId,
    [property: Id(2)] InoOperationPhase Phase,
    [property: Id(3)] long OperationVersion,
    [property: Id(4)] string EventType,
    [property: Id(5)] DateTimeOffset OccurredAt)
{
    public const int CurrentProjectionSchemaVersion = 1;
    public const string PhaseEventType = "ino.operation.phase.v1";
    [Id(6)] public int ProjectionSchemaVersion { get; init; } = CurrentProjectionSchemaVersion;
    [Id(7)] public string ConversationId { get; init; } = string.Empty;
    [Id(8)] public long ConversationRevision { get; init; }
    [Id(9)] public string RequestId { get; init; } = string.Empty;
    [Id(10)] public string ConversationGrainKey { get; init; } = string.Empty;
    [Id(11)] public OperationFeedView? View { get; init; }
    [Id(12)] public string? ToolId { get; init; }
    [Id(13)] public string? EffectId { get; init; }
    [Id(14)] public string? ApprovalId { get; init; }
    [Id(15)] public WorkflowReference? Workflow { get; init; }
    public static OperationOutboxRecord Create(
        string eventId,
        string operationId,
        InoOperationPhase phase,
        long operationVersion,
        DateTimeOffset occurredAt,
        string conversationId,
        long conversationRevision,
        string requestId,
        string conversationGrainKey,
        OperationFeedView view,
        string? toolId = null,
        string? effectId = null,
        string? approvalId = null,
        WorkflowReference? workflow = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationGrainKey);
        ArgumentNullException.ThrowIfNull(view);
        if (!Enum.IsDefined(phase)) throw new ArgumentOutOfRangeException(nameof(phase));
        if (operationVersion < 1 || conversationRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(operationVersion));
        var normalizedAction = view.Action is { } action && OAuthCallbackPaths.IsStructurallyValidAction(action) ? action : null;
        var normalized = view with
        {
            State = StateFor(phase),
            ApprovalId = approvalId ?? view.ApprovalId,
            Action = normalizedAction,
            Turns = (view.Turns ?? []).TakeLast(16).Select(turn => turn with { }).ToArray()
        };
        return new(eventId, operationId, phase, operationVersion, PhaseEventType, occurredAt)
        {
            ConversationId = conversationId,
            ConversationRevision = conversationRevision,
            RequestId = requestId,
            ConversationGrainKey = conversationGrainKey,
            View = normalized,
            ToolId = toolId,
            EffectId = effectId,
            ApprovalId = normalized.ApprovalId,
            Workflow = workflow
        };
    }
    public byte[] ToPayloadUtf8() => JsonSerializer.SerializeToUtf8Bytes(this);
    public static bool TryRead(byte[] payloadUtf8, out OperationOutboxRecord? record)
    {
        record = null;
        if (payloadUtf8 is null || payloadUtf8.Length == 0) return false;
        try
        {
            var candidate = JsonSerializer.Deserialize<OperationOutboxRecord>(payloadUtf8);
            if (candidate is null) return false;
            if (candidate.IsCurrent())
            {
                record = candidate;
                return true;
            }
            if (candidate.View is not { Turns.Length: > 16 } legacyView) return false;
            var repaired = candidate with
            {
                View = legacyView with
                {
                    Turns = legacyView.Turns.TakeLast(16).Select(turn => turn with { }).ToArray()
                }
            };
            if (!repaired.IsCurrent()) return false;
            record = repaired;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
    public InoConversationSnapshot ToSnapshot()
    {
        if (!IsCurrent()) throw new InvalidOperationException("The operation projection is not a current canonical record.");
        var view = View!;
        var action = view.Action is { } candidate && OAuthCallbackPaths.IsStructurallyValidAction(candidate) ? candidate : null;
        return new(
            ConversationId,
            checked((int)Math.Min(ConversationRevision, int.MaxValue)),
            view.Turns.Select(turn => new InoConversationTurn(turn.CommandId, turn.Role, turn.Text, turn.State)).ToArray(),
            [new InoConversationOperation(
                OperationId,
                view.CommandId,
                string.Empty,
                view.State,
                view.SafeReason,
                view.Retryable,
                OccurredAt,
                action,
                null,
                null,
                OperationVersion,
                Workflow,
                view.ApprovalId,
                Phase)]);
    }
    private bool IsCurrent() =>
        ProjectionSchemaVersion == CurrentProjectionSchemaVersion && string.Equals(EventType, PhaseEventType, StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(EventId) &&
        !string.IsNullOrWhiteSpace(OperationId) &&
        !string.IsNullOrWhiteSpace(ConversationId) &&
        !string.IsNullOrWhiteSpace(RequestId) &&
        !string.IsNullOrWhiteSpace(ConversationGrainKey) &&
        Enum.IsDefined(Phase) && OperationVersion > 0 && ConversationRevision >= 0 && View is { Turns: not null } &&
        string.Equals(View.State, StateFor(Phase), StringComparison.Ordinal) &&
        View.Turns.Length <= 16;
    private static string StateFor(InoOperationPhase phase) => phase switch
    {
        InoOperationPhase.Accepted or InoOperationPhase.Queued or InoOperationPhase.Approved => InoConversationStates.Queued,
        InoOperationPhase.Running or InoOperationPhase.ApplyingEffect => InoConversationStates.Running,
        InoOperationPhase.AwaitingAuthorization => InoConversationStates.AwaitingAuthorization,
        InoOperationPhase.RetryScheduled => InoConversationStates.RetryScheduled,
        InoOperationPhase.Succeeded => InoConversationStates.Succeeded,
        InoOperationPhase.OutcomeUnknown => InoConversationStates.OutcomeUnknown,
        InoOperationPhase.Cancelled => InoConversationStates.Cancelled,
        InoOperationPhase.AwaitingApproval => InoConversationStates.AwaitingApproval,
        _ => InoConversationStates.Failed
    };
}
[GenerateSerializer, Alias("digitalbrain.runtime.workflow-reference")]
public sealed record WorkflowReference([property: Id(0)] string Runner, [property: Id(1)] string WorkflowId, [property: Id(2)] string SessionId);
public sealed record InoAuthorizationResume(string Provider, string ToolId, string AuthorizationAttemptId, DateTimeOffset ExpiresAt);
[GenerateSerializer, Alias("digitalbrain.runtime.ino-authorization-request")]
public sealed record InoAuthorizationRequest(
    [property: Id(0)] string Provider,
    [property: Id(1)] string ToolId,
    [property: Id(2)] string AuthorizationAttemptId,
    [property: Id(3)] DateTimeOffset ExpiresAt,
    [property: Id(4)] string AuthorizationFlowReference,
    [property: Id(5)] string SafeSummary);
public sealed record InoWorkflowRequest(
    string OperationId,
    string ConversationId,
    string Prompt,
    IReadOnlyList<string> History,
    string RequestId,
    InoAuthorizationResume? AuthorizationResume = null,
    WorkflowReference? PriorWorkflow = null,
    string? ActorScope = null,
    BrainOwnerId? OwnerId = null,
    ActorId? ActorId = null);
public enum InoToolAccess { Read, Mutation }
public sealed record InoToolRequest(string ToolId, InoToolAccess Access, string Scope, string SafeSummary);
public sealed record InoApprovedTool(string ToolId, string Scope, string SafeSummary);
public sealed record InoToolEffectRequest(string OperationId, string EffectId, string ToolId, string Scope, string ActorScope, string ProviderIdempotencyKey);
public enum InoToolEffectDisposition { Succeeded, Failed, OutcomeUnknown }
[GenerateSerializer, Alias("digitalbrain.runtime.ino-tool-effect-result")]
public sealed record InoToolEffectResult([property: Id(0)] InoToolEffectDisposition Disposition, [property: Id(1)] string SafeResult);
public sealed record InoWorkflowResult(string Text, WorkflowReference Workflow, InoToolRequest? ToolRequest = null, InoAuthorizationRequest? AuthorizationRequest = null);
public interface IAgentWorkflowRunner
{
    Task<InoWorkflowResult> ExecuteAsync(InoWorkflowRequest request, CancellationToken cancellationToken = default);
}
public static class InoOperationPhases
{
    public static InoOperationPhase FromConversationStatus(string state) => state switch
    {
        InoConversationStates.Queued => InoOperationPhase.Queued,
        InoConversationStates.Running => InoOperationPhase.Running,
        InoConversationStates.AwaitingApproval => InoOperationPhase.AwaitingApproval,
        InoConversationStates.AwaitingAuthorization => InoOperationPhase.AwaitingAuthorization,
        InoConversationStates.Succeeded => InoOperationPhase.Succeeded,
        InoConversationStates.OutcomeUnknown => InoOperationPhase.OutcomeUnknown,
        InoConversationStates.Cancelled => InoOperationPhase.Cancelled,
        InoConversationStates.RetryScheduled => InoOperationPhase.RetryScheduled,
        _ => InoOperationPhase.Failed
    };
}
