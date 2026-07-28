using System.Runtime.CompilerServices;
using DigitalBrain.Kernel;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

public abstract class Agent : Neuron, IAgent
{
    private readonly IChatClient _toolCallingClient;

    protected Agent(IChatClient chatClient)
    {
        ArgumentNullException.ThrowIfNull(chatClient);

        _toolCallingClient = new ChatClientBuilder(chatClient)
            .UseFunctionInvocation()
            .Build();
    }

    protected abstract IReadOnlyList<CapabilityTool> Tools { get; }

    protected virtual string? Instructions => null;

    protected static CapabilityTool Capability(string name, string description, Delegate invoke)
        => new(name, description, invoke);

    public async IAsyncEnumerable<ChatResponseUpdate> RespondStreaming(
        IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var turnScheduler = TaskScheduler.Current;
        var options = new ChatOptions
        {
            Tools = [.. Tools.Select(tool => tool.BindTo(turnScheduler))],
            ToolMode = ChatToolMode.Auto,
        };
        var instructions = Instructions;
        IReadOnlyList<ChatMessage> request = string.IsNullOrWhiteSpace(instructions)
            ? messages
            : [new ChatMessage(ChatRole.System, instructions), .. messages];

        var selected = new List<string>();

        await foreach (var update in _toolCallingClient
            .GetStreamingResponseAsync(request, options, cancellationToken))
        {
            selected.AddRange(update.Contents.OfType<FunctionCallContent>().Select(call => call.Name));
            yield return update;
        }

        foreach (var capability in selected)
        {
            await EmitAsync(new CapabilityToolSelected(capability));
        }
    }

    public Task<ChatResponse> Respond(IReadOnlyList<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        return RespondStreaming(messages).ToChatResponseAsync();
    }
}
