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

    protected static CapabilityTool Capability(string name, string description, Delegate invoke)
        => new(name, description, invoke);

    public async Task<ChatResponse> Respond(IReadOnlyList<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var turnScheduler = TaskScheduler.Current;
        var options = new ChatOptions
        {
            Tools = [.. Tools.Select(tool => tool.BindTo(turnScheduler))],
            ToolMode = ChatToolMode.Auto,
        };

        var response = await _toolCallingClient.GetResponseAsync(messages, options);

        foreach (var selected in SelectedCapabilities(response))
        {
            await EmitAsync(new CapabilityToolSelected(selected));
        }

        return response;
    }

    private static IEnumerable<string> SelectedCapabilities(ChatResponse response)
        => response.Messages
            .SelectMany(message => message.Contents)
            .OfType<FunctionCallContent>()
            .Select(call => call.Name);
}
