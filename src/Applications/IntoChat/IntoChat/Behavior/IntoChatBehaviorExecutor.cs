using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.AI;
using DigitalBrain.AI.Web;
using DigitalBrain.Core.Behavior;
using DigitalBrain.Flutter;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace IntoChat;

internal sealed class IntoChatBehaviorExecutor(IServiceProvider services, INeuronInvoker invoker,
    BehaviorCodeRunner codeRunner, BehaviorLiveEvents events) : IBehaviorNodeExecutor
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<JsonElement> ExecuteAsync(BehaviorExecutionContext context, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, events.BeginExecution(context.BehaviorId, context.RunId));
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            return await ExecuteCoreAsync(context, linked.Token);
        }
        finally { events.EndExecution(context.BehaviorId, context.RunId); }
    }

    private async Task<JsonElement> ExecuteCoreAsync(BehaviorExecutionContext context, CancellationToken cancellationToken)
    {
        if (string.Equals(context.Node.Kind, "code", StringComparison.OrdinalIgnoreCase))
        {
            // Source is authored code, never a template for untrusted incoming values.
            return await codeRunner.RunAsync(context.Node.Config.GetProperty("source").GetString()!, context.Value, cancellationToken);
        }
        var config = BehaviorExpressions.Resolve(context.Node.Config, context.Input, context.Value, context.Outputs);
        switch (context.Node.Kind.ToLowerInvariant())
        {
            case "call":
                return await CallAsync(context, config, cancellationToken);
            case "agent":
                if (config.TryGetProperty("mode", out var dynamicMode) && dynamicMode.GetString() is "spawn" or "send" or "collect" or "stop")
                {
                    return await services.GetRequiredService<BehaviorAgents>().ExecuteAsync(context, config, cancellationToken);
                }
                if (config.TryGetProperty("mode", out var webMode) && webMode.GetString() == "web")
                {
                    var web = services.GetRequiredService<PlaywrightWebAgent>();
                    if (config.TryGetProperty("operation", out var operation) && operation.GetString() == "company")
                    {
                        return await web.LookupCompanyAsync(OptionalText(config, "company")
                            ?? throw new ArgumentException("Company lookup needs a company name."), OptionalText(config, "website"), cancellationToken);
                    }
                    return await web.ResearchAsync(OptionalText(config, "prompt") ?? Text(context.Value), OptionalText(config, "startUrl"), cancellationToken);
                }
                if (config.TryGetProperty("mode", out var mode) && mode.GetString() == "conversation")
                {
                    return await ConverseAsync(context, config, cancellationToken);
                }
                var instructions = config.TryGetProperty("instructions", out var instruction) ? instruction.GetString() : "Process the input and answer concisely.";
                var prompt = config.TryGetProperty("prompt", out var promptValue) ? Text(promptValue) : Text(context.Value);
                var response = await services.GetRequiredService<IChatClient>().GetResponseAsync(
                    [new ChatMessage(ChatRole.System, instructions), new ChatMessage(ChatRole.User, prompt)],
                    cancellationToken: cancellationToken);
                return JsonSerializer.SerializeToElement(new { text = response.Text }, Json);
            default:
                throw new ArgumentException($"Unsupported executable neuron kind '{context.Node.Kind}'.");
        }
    }

    private async Task<JsonElement> CallAsync(BehaviorExecutionContext context, JsonElement config, CancellationToken cancellationToken)
    {
        var address = config.GetProperty("neuron").GetString();
        if (!NeuronId.TryParse(address, out var neuron)) { throw new ArgumentException("The call neuron address is invalid."); }
        var contract = config.GetProperty("interface").GetString()!;
        var method = config.GetProperty("method").GetString()!;
        var descriptor = invoker.Describe(contract, method);
        var arguments = config.TryGetProperty("arguments", out var supplied) ? JsonNode.Parse(supplied.GetRawText())!.AsObject() : new JsonObject();
        if (!descriptor.IsReadOnly)
        {
            // Retries of this step carry the identical command id into specialist modules.
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{context.BehaviorId}/{context.RunId}/{context.Version}/{context.Node.Id}"));
            var id = new CommandId(new Guid(hash.AsSpan(0, 16)));
            var property = invoker.ArgumentContractOf(contract, method)?.CommandIdPropertyName
                ?? throw new ArgumentException("Mutating neuron calls require an idempotent command contract.");
            arguments[property] = JsonSerializer.SerializeToNode(id, Json);
        }
        return await invoker.InvokeAsync(neuron, contract, method, JsonSerializer.SerializeToElement(arguments, Json), cancellationToken)
            ?? JsonSerializer.SerializeToElement<object?>(null);
    }

    private async Task<JsonElement> ConverseAsync(BehaviorExecutionContext context, JsonElement config, CancellationToken cancellationToken)
    {
        var tools = new List<AITool>();
        if (services.GetService<TableService>() is { } tables) { tools.AddRange(new TableAgentTools(tables).Create()); }
        tools.AddRange(new WorkspaceAgentTools(services.GetRequiredService<WorkspaceArtifactStore>()).Create());
        tools.AddRange(services.GetRequiredService<BehaviorTools>().AgentTools());
        tools.AddRange(services.GetRequiredService<BehaviorAgentTools>().CreateTools());
        var extra = config.TryGetProperty("instructions", out var instruction) ? instruction.GetString() : null;
        var agent = ConversationalAgent.Create(services, "IntoChat", tools, extra);
        var messages = new List<ChatMessage>();
        if (context.Value.ValueKind == JsonValueKind.Object && context.Value.TryGetProperty("messages", out var inputMessages)
            && inputMessages.ValueKind == JsonValueKind.Array)
        {
            messages.AddRange(BehaviorConversationHistory.Read(inputMessages));
        }
        else { messages.Add(new ChatMessage(ChatRole.User, Text(context.Value))); }

        var text = new StringBuilder();
        var updates = new List<AgentResponseUpdate>();
        var messageId = $"{context.RunId}-{context.Node.Id}";
        events.Publish(context.RunId, new { type = "TEXT_MESSAGE_START", messageId, role = "assistant" });
        await foreach (var update in agent.RunStreamingAsync(messages, cancellationToken: cancellationToken))
        {
            updates.Add(update);
            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case TextContent chunk when chunk.Text.Length > 0:
                        text.Append(chunk.Text);
                        events.Publish(context.RunId, new { type = "TEXT_MESSAGE_CONTENT", messageId, delta = chunk.Text });
                        break;
                    case FunctionCallContent call:
                        events.Publish(context.RunId, new { type = "TOOL_CALL_START", toolCallId = call.CallId, toolCallName = call.Name, parentMessageId = messageId });
                        events.Publish(context.RunId, new { type = "TOOL_CALL_ARGS", toolCallId = call.CallId, delta = JsonSerializer.Serialize(call.Arguments, Json) });
                        break;
                    case FunctionResultContent result:
                        events.Publish(context.RunId, new { type = "TOOL_CALL_RESULT", toolCallId = result.CallId, content = JsonSerializer.Serialize(result.Result, Json) });
                        events.Publish(context.RunId, new { type = "TOOL_CALL_END", toolCallId = result.CallId });
                        break;
                }
            }
        }
        events.Publish(context.RunId, new { type = "TEXT_MESSAGE_END", messageId });
        var response = updates.ToAgentResponse();
        messages.AddRange(response.Messages);
        var answer = text.ToString();
        var historyBudget = Math.Min(14_000, 20_000 - Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(answer, Json)) - 100);
        BehaviorConversationHistory.Trim(messages, historyBudget);
        return JsonSerializer.SerializeToElement(new { text = answer, history = messages }, Json);
    }

    private static string Text(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
    private static string? OptionalText(JsonElement config, string property) => config.TryGetProperty(property, out var item)
        && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
}

internal static class BehaviorConversationHistory
{
    internal const int InputBudget = 8_000;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static string Answer(JsonElement output)
        => output.ValueKind == JsonValueKind.Object && output.TryGetProperty("text", out var text)
            ? text.GetString() ?? string.Empty
            : output.ValueKind == JsonValueKind.String ? output.GetString()! : output.GetRawText();

    internal static List<ChatMessage> Read(JsonElement value)
    {
        var messages = new List<ChatMessage>();
        foreach (var message in value.EnumerateArray())
        {
            if (message.TryGetProperty("contents", out _))
            {
                messages.Add(JsonSerializer.Deserialize<ChatMessage>(message, Json)
                    ?? throw new ArgumentException("A conversation message is empty."));
            }
            else
            {
                var role = message.GetProperty("role").GetString() == "assistant" ? ChatRole.Assistant : ChatRole.User;
                messages.Add(new ChatMessage(role, message.GetProperty("content").GetString()));
            }
        }
        return messages;
    }

    internal static void Trim(List<ChatMessage> messages, int maximumBytes)
    {
        if (Size(messages) <= maximumBytes)
        {
            return;
        }

        // Large table pages and schemas are reproducible observations. Keep their identities
        // and metadata, label omitted data, and retain matching function call/result pairs.
        foreach (var message in messages)
        {
            for (var index = 0; index < message.Contents.Count; index++)
            {
                if (message.Contents[index] is FunctionResultContent result)
                {
                    var body = JsonSerializer.SerializeToElement(result.Result, Json);
                    if (Encoding.UTF8.GetByteCount(body.GetRawText()) > 2_000)
                    {
                        message.Contents[index] = new FunctionResultContent(result.CallId, Compact(body));
                    }
                }
            }
        }
        while (Size(messages) > maximumBytes)
        {
            var nextTurn = messages.Count > 1 ? messages.FindIndex(1, message => message.Role == ChatRole.User) : -1;
            if (nextTurn < 0)
            {
                break;
            }
            messages.RemoveRange(0, nextTurn);
        }
        if (Size(messages) > maximumBytes)
        {
            throw new ArgumentException("This conversation turn exceeds the durable message budget. Send a shorter message or refer to saved artifacts instead of embedding their contents.");
        }
    }

    private static int Size(List<ChatMessage> messages) => Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(messages, Json));

    private static JsonElement Compact(JsonElement value)
    {
        var summary = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
            {
                if (property.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False
                    || property.Value.ValueKind == JsonValueKind.String && property.Value.GetString()!.Length <= 400)
                {
                    summary[property.Name] = property.Value.Clone();
                }
            }
        }
        summary["historyCompacted"] = JsonSerializer.SerializeToElement(true, Json);
        summary["note"] = JsonSerializer.SerializeToElement("Large tool data omitted from conversation memory. Use the read tool with the retained ID to obtain current contents.", Json);
        return JsonSerializer.SerializeToElement(summary, Json);
    }
}
