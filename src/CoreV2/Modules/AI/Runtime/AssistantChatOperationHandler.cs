using System.Text.Json;
using Brain.Abstractions.Runtime;
using Brain.Core.Runtime;
using Brain.Modules.AI.Contracts;

namespace Brain.Modules.AI;

public sealed class AssistantChatOperationHandler : IBrainOperationHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string InputSchema = """
        {"type":"object","additionalProperties":false,"properties":{"message":{"type":"string"}},"required":["message"]}
        """;
    private const string ResultSchema = """
        {"type":"object","additionalProperties":false,"properties":{"response":{"type":"string"},"tools":{"type":"array"}},"required":["response","tools"]}
        """;

    public BrainOperationDescriptor Descriptor { get; } = new(
        "Assistant.Chat@1",
        "ai",
        "Chat with the operation-using assistant",
        InputSchema,
        ResultSchema);

    public Task<string> ExecuteAsync(
        BrainOperationExecutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var input = JsonSerializer.Deserialize<AssistantChatInput>(
            context.Invocation.InputJson,
            JsonOptions);
        if (input is null || string.IsNullOrWhiteSpace(input.Message))
        {
            throw new JsonException("Assistant.Chat@1 requires a non-empty message.");
        }

        return context.Grains
            .GetGrain<IAssistantNeuron>(
                $"{context.Invocation.WorkspaceId}/{context.Invocation.PrincipalId}/assistant")
            .ChatAsync(new AssistantNeuronRequest(
                context.ActivityId,
                context.Invocation,
                input.Message.Trim()));
    }
}
