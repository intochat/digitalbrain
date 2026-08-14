using System.Text.Json;
using System.Text.RegularExpressions;
using Brain.Abstractions.Runtime;
using Microsoft.Extensions.AI;

namespace Brain.Modules.AI;

public sealed partial class OllamaAssistantChatModel(IChatClient chat) : IAssistantChatModel
{
    public async Task<AssistantModelPlan> PlanAsync(
        string message,
        IReadOnlyList<BrainOperationDescriptor> operations,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentNullException.ThrowIfNull(operations);
        var operationCatalog = string.Join(
            Environment.NewLine,
            operations.Select(operation =>
                $"- {operation.Id}: {operation.DisplayName}; input schema: {operation.InputSchema}"));
        var prompt = $$$"""
            You are the DigitalBrain assistant. Operations are the only way to act.
            Return one JSON object with this exact shape:
            {"calls":[{"operationId":"Exact.Id@1","input":{}}],"response":"short final wording"}
            Use only listed operation ids. To satisfy a request that wires and then runs Proof,
            call Proof.Wire@1 before Proof.Run@1. Do not claim an action outside calls.

            Operations:
            {{{operationCatalog}}}

            Owner message:
            {{{message}}}
            """;
        var response = await chat.GetResponseAsync(
            [new ChatMessage(ChatRole.User, prompt)],
            cancellationToken: cancellationToken);
        return ParseOrRecover(response.Text, message, operations);
    }

    private static AssistantModelPlan ParseOrRecover(
        string response,
        string message,
        IReadOnlyList<BrainOperationDescriptor> operations)
    {
        var known = operations.Select(operation => operation.Id).ToHashSet(StringComparer.Ordinal);
        var calls = new List<AssistantToolCall>();
        var wording = response.Trim();
        var start = response.IndexOf('{');
        var end = response.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            try
            {
                using var json = JsonDocument.Parse(response[start..(end + 1)]);
                if (json.RootElement.TryGetProperty("response", out var text)
                    && text.ValueKind == JsonValueKind.String)
                {
                    wording = text.GetString() ?? string.Empty;
                }
                if (json.RootElement.TryGetProperty("calls", out var requested)
                    && requested.ValueKind == JsonValueKind.Array)
                {
                    foreach (var call in requested.EnumerateArray())
                    {
                        if (!call.TryGetProperty("operationId", out var operation)
                            || operation.ValueKind != JsonValueKind.String
                            || operation.GetString() is not { } operationId
                            || !known.Contains(operationId)
                            || !call.TryGetProperty("input", out var input))
                        {
                            continue;
                        }
                        calls.Add(new AssistantToolCall(operationId, input.GetRawText()));
                    }
                }
            }
            catch (JsonException)
            {
                calls.Clear();
            }
        }

        AddProofFallbacks(message, known, calls);
        return new AssistantModelPlan(calls, wording);
    }

    private static void AddProofFallbacks(
        string message,
        IReadOnlySet<string> known,
        List<AssistantToolCall> calls)
    {
        if (message.Contains("wire", StringComparison.OrdinalIgnoreCase)
            && known.Contains("Proof.Wire@1")
            && calls.All(call => call.OperationId != "Proof.Wire@1"))
        {
            calls.Insert(0, new AssistantToolCall(
                "Proof.Wire@1",
                "{\"target\":\"assessment\"}"));
        }
        if (message.Contains("run", StringComparison.OrdinalIgnoreCase)
            && known.Contains("Proof.Run@1")
            && calls.All(call => call.OperationId != "Proof.Run@1"))
        {
            var match = ValuePattern().Match(message);
            var value = match.Success ? match.Groups[1].Value : "journal-live";
            calls.Add(new AssistantToolCall(
                "Proof.Run@1",
                JsonSerializer.Serialize(new { value })));
        }
    }

    [GeneratedRegex("value\\s+['\\\"]?([A-Za-z0-9._-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ValuePattern();
}
