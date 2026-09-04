using System.Runtime.CompilerServices;
using System.Text;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Core;
using DigitalBrain.Product.Interactions;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

// LLM turn + optional MCP. Agent turns must not call IDigitalBrain: nested
// BrainNeuron.Send deadlocks. In-silo callers use IAgentKernel on this grain.
public abstract class Agent : Neuron, IAgent, IAgentKernel
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

    public async Task HandleAsync(AgentRequest signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        var reply = await Ask(signal, cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await ReplyAsync(reply).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public async Task<AgentReply> Ask(AgentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var text = new StringBuilder();
        await foreach (var chunk in AskStreaming(
            [new ChatMessage(ChatRole.User, request.Text)],
            cancellationToken).ConfigureAwait(true))
        {
            text.Append(chunk.Text);
        }

        return new AgentReply(text.ToString());
    }

    public async IAsyncEnumerable<ChatResponseUpdate> AskStreaming(
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

    public Task InvalidateMcpTools() => Task.CompletedTask;
}
