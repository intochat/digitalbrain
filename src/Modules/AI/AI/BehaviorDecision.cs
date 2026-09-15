using System.Text.Json;
using DigitalBrain.Core.Behaviors;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

// A decision produces one schema-checked value. It has no agent loop or capabilities.
internal sealed class BehaviorDecision(IServiceProvider services) : IBehaviorDecision
{
    public async Task<string> DecideAsync(
        string instructions, string input, string outputSchema, string? provider, string? model,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var schemaErrors = BehaviorSchema.CheckSchema(outputSchema);
        if (schemaErrors.Count != 0)
        {
            throw new InvalidOperationException($"Decision output schema is invalid: {string.Join(" ", schemaErrors)}");
        }

        using var schema = JsonDocument.Parse(outputSchema);
        var client = Providers.Resolve(services, provider, model);
        // This middleware loops even with an empty tool list. Its inner client is not
        // publicly accessible, and changing its settings would affect other callers.
        if (client is FunctionInvokingChatClient || client.GetService<FunctionInvokingChatClient>() is not null)
        {
            throw new InvalidOperationException("Decisions require a chat client without function-invocation middleware. Configure a direct model provider for this decision.");
        }

        var response = await client.GetResponseAsync([
            new ChatMessage(ChatRole.System,
                $"{instructions}\nReturn exactly one JSON value matching the following JSON Schema. No tools are available.\n{outputSchema}"),
            new ChatMessage(ChatRole.User, input)],
            new ChatOptions
            {
                ModelId = model,
                Tools = [],
                ToolMode = ChatToolMode.None,
                ResponseFormat = ChatResponseFormat.ForJsonSchema(schema.RootElement.Clone(), "behavior_decision")
            }, cancellationToken).ConfigureAwait(false);

        if (response.Messages.SelectMany(message => message.Contents).Any(content => content is FunctionCallContent))
        {
            throw new InvalidOperationException("Decision response attempted a function call; decisions can only return a JSON value.");
        }

        var text = response.Text;
        var outputErrors = BehaviorSchema.Validate(outputSchema, text);
        if (outputErrors.Count != 0)
        {
            throw new InvalidOperationException($"Decision response does not match its output schema: {string.Join(" ", outputErrors)}");
        }

        return text;
    }
}
