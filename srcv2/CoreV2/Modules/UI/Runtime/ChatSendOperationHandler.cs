using System.Text.Json;
using Brain.Abstractions.Journal;
using Brain.Abstractions.Runtime;
using Brain.Core.Runtime;
using Brain.Modules.AI.Contracts;
using Brain.Modules.UI.Contracts;

namespace Brain.Modules.UI;

public sealed class ChatSendOperationHandler : IBrainOperationHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string InputSchema = """
        {"type":"object","additionalProperties":false,"properties":{"message":{"type":"string"}},"required":["message"]}
        """;
    private const string ResultSchema = """
        {"type":"object","additionalProperties":false,"properties":{"response":{"type":"string"},"tools":{"type":"array"}},"required":["response","tools"]}
        """;

    public BrainOperationDescriptor Descriptor { get; } = new(
        "Chat.Send@1",
        "ui",
        "Send a message through the operation-using assistant",
        InputSchema,
        ResultSchema);

    public async Task<string> ExecuteAsync(
        BrainOperationExecutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var input = JsonSerializer.Deserialize<ChatSendInput>(context.Invocation.InputJson, JsonOptions);
        if (input is null || string.IsNullOrWhiteSpace(input.Message))
        {
            throw new JsonException("Chat.Send@1 requires a non-empty message.");
        }

        var message = input.Message.Trim();
        await context.JournalAsync(
            "chat-user-message",
            "ui/chat/principal",
            BrainJournalDirection.Inbound,
            "Chat.UserMessage@1",
            "received",
            message);
        var assistant = await context.Grains
            .GetGrain<IBrainRuntimeGrain>("brain")
            .InvokeWithinActivityAsync(
                context.ActivityId,
                new BrainOperationInvocation(
                    "Assistant.Chat@1",
                    JsonSerializer.Serialize(new AssistantChatInput(message), JsonOptions),
                    context.Invocation.WorkspaceId,
                    context.Invocation.PrincipalId,
                    $"{context.Invocation.IdempotencyKey}:assistant"));
        var assistantResult = JsonSerializer.Deserialize<AssistantChatResult>(
            assistant.ResultJson,
            JsonOptions)
            ?? throw new InvalidOperationException("Assistant.Chat@1 returned no result.");
        var result = new ChatTurnResult(
            assistantResult.Response,
            assistantResult.Tools
                .Select(tool => new ChatToolResult(tool.OperationId, tool.ResultJson))
                .ToArray());
        await context.JournalAsync(
            "chat-assistant-message",
            "ui/chat/principal",
            BrainJournalDirection.Outbound,
            "Chat.AssistantMessage@1",
            "completed",
            result.Response);
        return JsonSerializer.Serialize(result, JsonOptions);
    }
}
