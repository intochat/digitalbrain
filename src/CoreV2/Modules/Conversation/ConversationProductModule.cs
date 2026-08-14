using System.Text.Json;
using Brain.Modules.Conversation.Contracts;
using Brain.Runtime.Abstractions;

namespace Brain.Modules.Conversation;

public sealed class ConversationProductModule(IGrainFactory grainFactory) : IRuntimeProductModule
{
    private const string SendInputSchema = """
        {"type":"object","additionalProperties":false,"properties":{"conversationId":{"type":"string"},"message":{"type":"string"}},"required":["message"]}
        """;
    private const string ReadInputSchema = """
        {"type":"object","additionalProperties":false,"properties":{"conversationId":{"type":"string"}}}
        """;
    private const string ResultSchema = """
        {"type":"object","properties":{"conversationId":{"type":"string"},"messages":{"type":"array"}},"required":["conversationId","messages"]}
        """;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IGrainFactory _grainFactory = grainFactory;

    public RuntimeModuleDescriptor Module { get; } = new(
        ConversationContracts.ModuleId,
        "Conversation",
        RuntimeModuleStatus.Ready);

    public IReadOnlyList<RuntimeOperationDescriptor> Operations { get; } =
    [
        new(
            ConversationContracts.SendOperationId,
            ConversationContracts.ModuleId,
            "Send conversation message",
            SendInputSchema,
            ResultSchema),
        new(
            ConversationContracts.ReadOperationId,
            ConversationContracts.ModuleId,
            "Read conversation",
            ReadInputSchema,
            ResultSchema),
    ];

    public async Task<string> ExecuteAsync(
        string operationId,
        string inputJson,
        RuntimeModuleExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        using var document = JsonDocument.Parse(inputJson);
        var root = RequireObject(document.RootElement);
        var conversationId = OptionalString(root, "conversationId") ?? "main";
        ValidateIdentifier(conversationId);
        var grain = _grainFactory.GetGrain<IConversationGrain>(
            ConversationGrainKey.Create(context.Workspace, conversationId));

        var snapshot = operationId switch
        {
            ConversationContracts.SendOperationId => await grain.AppendAsync(new ConversationAppendRequest(
                context.Principal,
                context.IdempotencyKey,
                RequiredString(root, "message"))),
            ConversationContracts.ReadOperationId => await grain.ReadAsync(),
            _ => throw new KeyNotFoundException($"Conversation operation '{operationId}' is not installed."),
        };
        return JsonSerializer.Serialize(snapshot, JsonOptions);
    }

    private static JsonElement RequireObject(JsonElement value)
        => value.ValueKind == JsonValueKind.Object
            ? value
            : throw new JsonException("Conversation input must be a JSON object.");

    private static string RequiredString(JsonElement root, string name)
        => OptionalString(root, name) is { Length: > 0 } value
            ? value
            : throw new JsonException($"Conversation input requires a non-empty '{name}'.");

    private static string? OptionalString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : throw new JsonException($"Conversation input '{name}' must be a string.");
    }

    private static void ValidateIdentifier(string conversationId)
    {
        if (conversationId.Length is < 1 or > 80
            || conversationId.Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new JsonException("Conversation id must contain only letters, digits, '-' or '_'.");
        }
    }
}
