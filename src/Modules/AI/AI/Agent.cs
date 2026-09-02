using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions.Interactions;
using DigitalBrain.Core;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

// The setup layer over a raw LLM: an agent owns its initial prompt and its
// toolset; model/provider concerns live in the injected chat client.
public abstract class Agent : Neuron, IAgent
{
    private readonly IChatClient _chatClient;

    protected Agent(NeuronRuntime runtime, IChatClient chatClient)
        : base(runtime)
    {
        ArgumentNullException.ThrowIfNull(chatClient);

        _chatClient = chatClient;
    }

    protected abstract string Instructions { get; }

    protected virtual IReadOnlyList<AITool> Tools => [];

    public async IAsyncEnumerable<ChatResponseUpdate> RespondStreaming(
        IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        cancellationToken.ThrowIfCancellationRequested();

        var tools = Tools;
        if (AgentTurnContext.Current?.AllowedToolNames is { } allowedToolNames)
        {
            var allowed = new HashSet<string>(allowedToolNames, StringComparer.Ordinal);
            // Apply the trusted continuation allowlist to every tool type, including
            // server-side tools. OAuth consent must never enable an automatic write.
            tools = [.. tools.Where(tool => allowed.Contains(tool.Name))];
        }
        var options = new ChatOptions { MaxOutputTokens = 4096 };
        if (tools.Count > 0)
        {
            var turnScheduler = TaskScheduler.Current;
            options.Tools = [.. tools.Select(tool =>
                tool is AIFunction capability ? new TurnBoundFunction(capability, turnScheduler) : tool)];
        }
        IReadOnlyList<ChatMessage> request = string.IsNullOrWhiteSpace(Instructions)
            ? messages
            : [new ChatMessage(ChatRole.System, Instructions), .. messages];

        await foreach (var update in _chatClient
            .GetStreamingResponseAsync(request, options, cancellationToken).ConfigureAwait(true))
        {
            yield return update;
        }
    }

    public Task<ChatResponse> Respond(IReadOnlyList<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        return RespondStreaming(messages).ToChatResponseAsync();
    }
}
