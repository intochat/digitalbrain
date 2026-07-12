using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DigitalBrain.Core.Runtime;

public static class ConversationSurfacePayload
{
    public const string HomeSurfaceId = "workspace-home";
    public const string SendBindingId = "ino.send";
    public const string SendActionType = "ino.interact";
    public const string SendInputSchema = "digitalbrain.ino.prompt-input.v2";
    public const string NewBindingId = "ino.new";
    public const string NewActionType = "ino.conversation.new";
    public const string DeleteBindingId = "ino.delete";
    public const string DeleteActionType = "ino.conversation.delete";
    public const string EmptyInputSchema = "digitalbrain.ino.empty-input.v1";

    public static readonly string[] RequiredCapabilities =
        ["ui.protocol.v2", "ui.payload.native", "ui.native.ino-conversation", "ui.native.typed-actions"];

    public static JsonElement Build(InoConversationSnapshot conversation)
    {
        var current = conversation.CurrentOperation;
        Dictionary<string, object?>? operation = current is null
            ? null
            : new Dictionary<string, object?>
            {
                ["state"] = current.State,
                ["retryable"] = current.Retryable
            };
        if (operation is not null && !string.IsNullOrWhiteSpace(current!.SafeReason))
            operation["safeReason"] = current.SafeReason;
        if (operation is not null && current!.Action is { } action)
        {
            operation["action"] = new Dictionary<string, object?>
            {
                ["kind"] = action.Kind,
                ["label"] = action.Label,
                ["target"] = action.Target
            };
        }

        return JsonSerializer.SerializeToElement(new
        {
            kind = "native",
            nativeKind = "inoConversation",
            data = new
            {
                intro = "Ask INO about this workspace. I can help you understand what’s here and decide what to do next.",
                messages = conversation.Turns.Select(static turn => new
                {
                    turnKey = TurnKey(turn),
                    role = turn.Role,
                    text = turn.Text,
                    state = turn.State
                }).ToArray(),
                operation
            }
        });
    }

    public static IReadOnlyList<StoredActionBinding> Actions(
        InoConversationSnapshot conversation,
        DateTimeOffset now)
    {
        var expiresAt = now.Add(UiProtocol.ActionTokenLifetime);
        var lifecycle = LifecycleActions(now);
        if (conversation.CurrentOperation is { } operation && InoConversationStates.IsActive(operation.State))
            return lifecycle;
        return
        [
            new(
                SendBindingId,
                SendActionType,
                SendInputSchema,
                "ui.action",
                1,
                expiresAt),
            .. lifecycle
        ];
    }

    public static StoredActionBinding[] LifecycleActions(DateTimeOffset now)
    {
        var expiresAt = now.Add(UiProtocol.ActionTokenLifetime);
        return
        [
            new(NewBindingId, NewActionType, EmptyInputSchema, "ui.action", 1, expiresAt),
            new(DeleteBindingId, DeleteActionType, EmptyInputSchema, "ui.action", 1, expiresAt)
        ];
    }

    private static string TurnKey(InoConversationTurn turn)
    {
        var source = Encoding.UTF8.GetBytes(turn.CommandId + "\0" + turn.Role);
        var hash = Convert.ToHexStringLower(SHA256.HashData(source));
        return "turn-" + hash[..24];
    }
}
