using System.Text.Json;
using Brain.Abstractions.Journal;
using Brain.Abstractions.Runtime;
using Brain.Core.Runtime;
using Brain.Modules.AI.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace Brain.Modules.AI;

public sealed class AssistantDurableNeuron(
    [FromKeyedServices("assistant-responses")] IDurableDictionary<Guid, string> responses,
    IAssistantChatModel model,
    IGrainFactory grains)
    : DurableGrain, IAssistantNeuron
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<string> ChatAsync(AssistantNeuronRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (responses.TryGetValue(request.ActivityId, out var previous))
        {
            return previous;
        }

        var activity = grains.GetGrain<IBrainActivityGrain>(
            $"{request.Invocation.WorkspaceId}/{request.ActivityId:n}");
        var context = new BrainOperationExecutionContext(
            request.ActivityId,
            request.Invocation,
            activity,
            grains);
        await context.JournalAsync(
            "assistant-user-message",
            "ai/assistant/principal",
            BrainJournalDirection.Assistant,
            "Assistant.UserMessage@1",
            "received",
            request.Message);

        var runtime = grains.GetGrain<IBrainRuntimeGrain>("brain");
        var operations = await runtime.GetOperationsAsync();
        var plan = await model.PlanAsync(request.Message, operations, CancellationToken.None);
        await context.JournalAsync(
            "assistant-model-plan",
            "ai/assistant/principal",
            BrainJournalDirection.Assistant,
            "Assistant.ModelPlan@1",
            "planned",
            $"Model selected {plan.Calls.Count} Operation(s)");

        var tools = new List<AssistantToolExecution>();
        for (var index = 0; index < plan.Calls.Count; index++)
        {
            var call = plan.Calls[index];
            if (string.Equals(call.OperationId, "Assistant.Chat@1", StringComparison.Ordinal)
                || operations.All(operation => operation.Id != call.OperationId))
            {
                throw new InvalidOperationException(
                    $"The assistant selected unavailable Operation '{call.OperationId}'.");
            }

            await context.JournalAsync(
                $"assistant-tool-selected:{index}",
                "ai/assistant/principal",
                BrainJournalDirection.Assistant,
                "Assistant.ToolSelected@1",
                "selected",
                call.OperationId);
            var result = await runtime.InvokeWithinActivityAsync(
                request.ActivityId,
                new BrainOperationInvocation(
                    call.OperationId,
                    call.InputJson,
                    request.Invocation.WorkspaceId,
                    request.Invocation.PrincipalId,
                    $"{request.Invocation.IdempotencyKey}:tool:{index}"));
            tools.Add(new AssistantToolExecution(result.OperationId, result.ResultJson));
            await context.JournalAsync(
                $"assistant-tool-result:{index}",
                "ai/assistant/principal",
                BrainJournalDirection.Assistant,
                "Assistant.ToolResult@1",
                "completed",
                $"{result.OperationId}: {result.ResultJson}");
        }

        var response = tools.Count == 0
            ? plan.Response
            : $"I invoked {string.Join(" and ", tools.Select(tool => tool.OperationId))}. "
                + $"Results: {string.Join("; ", tools.Select(tool => tool.ResultJson))}";
        var resultJson = JsonSerializer.Serialize(
            new AssistantChatResult(response, tools),
            JsonOptions);
        await context.JournalAsync(
            "assistant-response",
            "ai/assistant/principal",
            BrainJournalDirection.Assistant,
            "Assistant.Response@1",
            "completed",
            response);
        responses.Add(request.ActivityId, resultJson);
        await WriteStateAsync();
        return resultJson;
    }
}
