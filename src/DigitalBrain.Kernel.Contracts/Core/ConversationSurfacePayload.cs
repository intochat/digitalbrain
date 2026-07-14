using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalBrain.Kernel.Contracts;

namespace DigitalBrain.Kernel.Contracts.Runtime;

public static class ConversationSurfacePayload
{
    private const int MaximumFeedTurns = 16;
    private const int MaximumFeedTurnUtf8Bytes = 2_048;
    private const int MaximumPayloadUtf8Bytes = 64 * 1024;
    public const string HomeSurfaceId = "workspace-home";
    public const string SendBindingId = "ino.send";
    public const string SendActionType = "ino.interact";
    public const string SendInputSchema = "digitalbrain.ino.prompt-input.v2";
    public const string ApprovalBindingId = "ino.approval.decision";
    public const string ApprovalActionType = "ino.approval.decision";
    public const string ApprovalInputSchema = "digitalbrain.ino.approval-decision.v1";

    public static readonly string[] RequiredCapabilities =
        ["ui.protocol.v2", "ui.payload.native", "ui.native.ino-conversation", "ui.native.typed-actions"];

    public static JsonElement Build(InoConversationSnapshot conversation)
    {
        var current = conversation.CurrentOperation;
        Dictionary<string, object?>? operation = current is null
            ? null
            : new Dictionary<string, object?>
            {
                ["operationId"] = current.OperationId,
                ["phase"] = ProjectionPhase(current.Phase, current.State),
                ["version"] = current.Version,
                ["state"] = current.State,
                ["retryable"] = current.Retryable
            };
        if (operation is not null && !string.IsNullOrWhiteSpace(current!.SafeReason))
            operation["safeReason"] = current.SafeReason;
        if (operation is not null && current!.State == InoConversationStates.AwaitingApproval &&
            !string.IsNullOrWhiteSpace(current.ApprovalId))
            operation["approvalId"] = current.ApprovalId;

        if (operation is not null && current!.Action is { } action && OAuthCallbackPaths.IsStructurallyValidAction(action))
        {
            operation["action"] = new Dictionary<string, object?> { ["kind"] = action.Kind, ["label"] = action.Label, ["target"] = action.Target };
        }

        var messages = conversation.Turns.TakeLast(MaximumFeedTurns).Select(static turn => new FeedMessage(TurnKey(turn), turn.Role, BoundedFeedText(turn.Text), turn.State))
            .ToList();
        var payload = Serialize(operation, messages);
        while (Encoding.UTF8.GetByteCount(payload.GetRawText()) > MaximumPayloadUtf8Bytes && messages.Count > 0)
        {
            messages.RemoveAt(0);
            payload = Serialize(operation, messages);
        }
        if (Encoding.UTF8.GetByteCount(payload.GetRawText()) > MaximumPayloadUtf8Bytes)
            throw new InvalidOperationException("The bounded INO surface payload exceeds the persistence contract.");
        return payload;
    }

    public static IReadOnlyList<StoredActionBinding> Actions(InoConversationSnapshot conversation, DateTimeOffset now) => Actions(conversation.CurrentOperation?.State, conversation.CurrentOperation?.ApprovalId, now);

    public static bool TryActions(JsonElement payload, DateTimeOffset now, out IReadOnlyList<StoredActionBinding> actions)
    {
        actions = [];
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty("kind", out var kind) ||
            kind.ValueKind != JsonValueKind.String ||
            !string.Equals(kind.GetString(), "native", StringComparison.Ordinal) ||
            !payload.TryGetProperty("nativeKind", out var nativeKind) ||
            nativeKind.ValueKind != JsonValueKind.String ||
            !string.Equals(nativeKind.GetString(), "inoConversation", StringComparison.Ordinal) ||
            !payload.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Object)
            return false;

        if (!data.TryGetProperty("operation", out var operation) || operation.ValueKind == JsonValueKind.Null)
        {
            actions = Actions(null, null, now);
            return true;
        }
        if (operation.ValueKind != JsonValueKind.Object || !operation.TryGetProperty("state", out var stateProperty) ||
            stateProperty.ValueKind != JsonValueKind.String)
            return false;

        var state = stateProperty.GetString();
        if (!IsKnownState(state)) return false;
        string? approvalId = null;
        if (operation.TryGetProperty("approvalId", out var approvalProperty) && approvalProperty.ValueKind == JsonValueKind.String)
            approvalId = approvalProperty.GetString();
        actions = Actions(state, approvalId, now);
        return true;
    }

    private static IReadOnlyList<StoredActionBinding> Actions(string? state, string? approvalId, DateTimeOffset now)
    {
        var expiresAt = now.Add(UiProtocol.ActionTokenLifetime);
        if (state == InoConversationStates.AwaitingApproval && !string.IsNullOrWhiteSpace(approvalId))
            return
            [new(ApprovalBindingId, ApprovalActionType, ApprovalInputSchema, "ui.action", 1, expiresAt)];
        if (state is not null && InoConversationStates.IsActive(state))
            return [];
        return
        [new(SendBindingId, SendActionType, SendInputSchema, "ui.action", 1, expiresAt)];
    }

    private static bool IsKnownState(string? state) => state is
        InoConversationStates.Idle or
        InoConversationStates.Queued or
        InoConversationStates.Running or
        InoConversationStates.Responding or
        InoConversationStates.AwaitingAuthorization or
        InoConversationStates.AwaitingApproval or
        InoConversationStates.RetryScheduled or
        InoConversationStates.Succeeded or
        InoConversationStates.Failed or
        InoConversationStates.OutcomeUnknown or
        InoConversationStates.Cancelled;

    private static string TurnKey(InoConversationTurn turn)
    {
        var source = Encoding.UTF8.GetBytes(turn.CommandId + "\0" + turn.Role);
        var hash = Convert.ToHexStringLower(SHA256.HashData(source));
        return "turn-" + hash[..24];
    }

    private static JsonElement Serialize(Dictionary<string, object?>? operation, IReadOnlyList<FeedMessage> messages) =>
        JsonSerializer.SerializeToElement(new
        {
            kind = "native",
            nativeKind = "inoConversation",
            data = new
            {
                intro = "Ask INO about this workspace. I can help you understand what’s here and decide what to do next.",
                messages,
                operation
            }
        });

    private static string BoundedFeedText(string value)
    {
        if (Encoding.UTF8.GetByteCount(value) <= MaximumFeedTurnUtf8Bytes) return value;
        const string ellipsis = "…";
        var remaining = MaximumFeedTurnUtf8Bytes - Encoding.UTF8.GetByteCount(ellipsis);
        var builder = new StringBuilder(value.Length);
        var written = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            var bytes = rune.Utf8SequenceLength;
            if (written + bytes > remaining) break;
            builder.Append(rune.ToString());
            written += bytes;
        }
        return builder.Append(ellipsis).ToString();
    }

    private static string ProjectionPhase(InoOperationPhase? phase, string state) => phase switch
    {
        InoOperationPhase.Accepted => "accepted",
        InoOperationPhase.Queued => "queued",
        InoOperationPhase.Running => "running",
        InoOperationPhase.AwaitingAuthorization => InoConversationStates.AwaitingAuthorization,
        InoOperationPhase.RetryScheduled => InoConversationStates.RetryScheduled,
        InoOperationPhase.Succeeded => InoConversationStates.Succeeded,
        InoOperationPhase.Failed => InoConversationStates.Failed,
        InoOperationPhase.OutcomeUnknown => InoConversationStates.OutcomeUnknown,
        InoOperationPhase.Cancelled => InoConversationStates.Cancelled,
        InoOperationPhase.AwaitingApproval => InoConversationStates.AwaitingApproval,
        InoOperationPhase.Approved => "approved",
        InoOperationPhase.ApplyingEffect => "applying-effect",
        _ => ProjectionPhase(state)
    };

    private static string ProjectionPhase(string state) => state switch
    {
        InoConversationStates.Queued => "accepted",
        InoConversationStates.Responding => "running",
        _ => state
    };

    private sealed record FeedMessage(
        [property: JsonPropertyName("turnKey")] string TurnKey,
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("state")] string State);
}
