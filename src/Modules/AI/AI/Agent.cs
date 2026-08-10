using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.AI;

public abstract class Agent : Neuron, IAgent
{
    private readonly IChatClient _toolCallingClient;

    protected Agent(IChatClient chatClient)
    {
        ArgumentNullException.ThrowIfNull(chatClient);

        _toolCallingClient = new ChatClientBuilder(chatClient)
            .UseFunctionInvocation(configure: static client => client.IncludeDetailedErrors = true)
            .Build();
    }

    protected virtual string? Instructions => null;

    protected virtual IReadOnlyList<AIFunction> AdditionalToolsFor(IReadOnlyList<ChatMessage> messages)
        => [];

    protected static AIFunction Capability(string name, string description, Delegate invoke)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(invoke);

        return AIFunctionFactory.Create(invoke, name, description);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> RespondStreaming(
        IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        cancellationToken.ThrowIfCancellationRequested();

        var turnScheduler = TaskScheduler.Current;
        var tools = ResolveTools(messages);
        var options = new ChatOptions
        {
            Tools = [.. tools.Select(tool => new TurnBoundFunction(tool, turnScheduler))],
            ToolMode = ChatToolMode.Auto,
            // Ollama defaults to a 4096-token window; a tool turn needs room for the
            // roster, several rounds, and thinking-model reasoning (num_ctx / num_predict).
            MaxOutputTokens = 4096,
            AdditionalProperties = new AdditionalPropertiesDictionary { ["num_ctx"] = 16384 },
        };
        var instructions = Instructions;
        IReadOnlyList<ChatMessage> request = string.IsNullOrWhiteSpace(instructions)
            ? messages
            : [new ChatMessage(ChatRole.System, instructions), .. messages];

        var selected = new List<string>();

        await foreach (var update in _toolCallingClient
            .GetStreamingResponseAsync(request, options, cancellationToken).ConfigureAwait(true))
        {
            selected.AddRange(update.Contents.OfType<FunctionCallContent>().Select(call => call.Name));
            yield return update;
        }

        foreach (var capability in selected)
        {
            await EmitAsync(new CapabilityToolSelected(capability)).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
    }

    public Task<ChatResponse> Respond(IReadOnlyList<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        return RespondStreaming(messages).ToChatResponseAsync();
    }

    public Task<ChatResponse> Respond(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);

        return RespondStreaming(messages, cancellationToken).ToChatResponseAsync(cancellationToken);
    }

    private IReadOnlyList<AIFunction> ResolveTools(IReadOnlyList<ChatMessage> messages)
    {
        var constant = new SystemTools(GrainFactory, Id.Owner, ServiceProvider).All();
        var additional = AdditionalToolsFor(messages);
        if (additional.Count == 0)
        {
            return constant;
        }

        var merged = new List<AIFunction>(constant.Count + additional.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tool in constant.Concat(additional))
        {
            if (seen.Add(tool.Name))
            {
                merged.Add(tool);
            }
        }

        return merged;
    }
}
