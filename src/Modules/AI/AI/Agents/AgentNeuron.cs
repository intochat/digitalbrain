using DigitalBrain.AI.Agents.Signals;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;

namespace DigitalBrain.AI.Agents;

// A durable conversational agent: Ask runs one turn against the selected chat client
// and publishes the answer as a typed signal.
[GrainType("agent")]
internal sealed class AgentNeuron : Neuron, IAgent
{
    public async Task<AgentReply> Ask(AgentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Message);

        var client = Providers.Resolve(ServiceProvider, request.Model?.Provider, request.Model?.Model, out var ownsClient);
        using var ownedClient = ownsClient ? client : null;
        var agent = new ChatClientAgent(
            client,
            new ChatClientAgentOptions
            {
                Name = this.GetPrimaryKeyString(),
                ChatOptions = new ChatOptions { Instructions = request.System },
            },
            ServiceProvider.GetService<ILoggerFactory>(),
            ServiceProvider);

        var session = await agent.CreateSessionAsync().ConfigureAwait(true);
        var response = await agent.RunAsync(request.Message, session).ConfigureAwait(true);
        var text = response.Text ?? string.Empty;

        await PublishAsync(new AgentReplied(this.GetPrimaryKeyString(), text, DateTimeOffset.UtcNow)).ConfigureAwait(true);
        return new AgentReply(text);
    }
}