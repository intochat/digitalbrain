using System.Text.Json;
using System.Text.Json.Serialization;

namespace DigitalBrain.Core.Runtime;

public static class InoConversationIdentity
{
    public static string From(RequestContext context) => "ino-" + RequestScope.Id(context);
}

public static class InoConversationStates
{
    public const string Idle = "idle";
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Responding = "responding";
    public const string AwaitingAuthorization = "awaiting-authorization";
    public const string AwaitingApproval = "awaiting-approval";
    public const string RetryScheduled = "retry-scheduled";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string OutcomeUnknown = "outcome-unknown";
    public const string Cancelled = "cancelled";

    public static bool IsActive(string state) =>
        state is Queued or Running or Responding or AwaitingAuthorization or AwaitingApproval or RetryScheduled;
}

public sealed record InoConversationTurn(
    string CommandId,
    string Role,
    string Text,
    string State);

[method: JsonConstructor]
public sealed record InoConversationOperation(
    string OperationId,
    string CommandId,
    string Prompt,
    string State,
    string? SafeReason,
    bool Retryable,
    DateTimeOffset UpdatedAt,
    ToolAction? Action = null,
    ToolGrounding? Grounding = null,
    IReadOnlyList<ToolGrounding>? Groundings = null,
    long Version = 0,
    WorkflowReference? Workflow = null,
    string? ApprovalId = null,
    InoOperationPhase? Phase = null)
{
    // Kept only for replaying pre-operation-id snapshots during the rolling deployment. New callers must
    // supply an operation id; the legacy command id remains a stable, scoped fallback until old snapshots age out.
    public InoConversationOperation(
        string commandId,
        string prompt,
        string state,
        string? safeReason,
        bool retryable,
        DateTimeOffset updatedAt,
        ToolAction? Action = null,
        ToolGrounding? Grounding = null,
        IReadOnlyList<ToolGrounding>? Groundings = null) : this(
            commandId,
            commandId,
            prompt,
            state,
            safeReason,
            retryable,
            updatedAt,
            Action,
            Grounding,
            Groundings)
    {
    }
}

public sealed record InoConversationSnapshot(
    string ConversationId,
    int Revision,
    IReadOnlyList<InoConversationTurn> Turns,
    IReadOnlyList<InoConversationOperation> Operations)
{
    public InoConversationOperation? CurrentOperation => Operations.LastOrDefault();

    public static InoConversationSnapshot Empty(RequestContext context) =>
        new(InoConversationIdentity.From(context), 0, [], []);
}

public sealed record ToolAction(string Kind, string Label, string Target);
public sealed record ToolGrounding(string ToolId, JsonElement Content);
