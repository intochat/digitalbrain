using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

public abstract class Agent : Neuron, IAgent
{
    private readonly INeuron[] _capabilities;
    private readonly ILLM _model;

    protected Agent(ILLM model, params INeuron[] capabilities)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(capabilities);

        foreach (var capability in capabilities)
        {
            ArgumentNullException.ThrowIfNull(capability);
        }

        _model = model;
        _capabilities = [.. capabilities];
    }

    protected abstract string Instructions { get; }

    public async Task<ChatResponse> RespondAsync(IReadOnlyList<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var agent = MafAgentFactory.Create(_model, Instructions, _capabilities);
        var session = await agent.CreateSessionAsync();
        var response = await agent.RunAsync(messages, session);

        return response.AsChatResponse();
    }
}
