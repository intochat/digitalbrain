using DigitalBrain.AI.Agents.Signals;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
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

        var text = string.Empty;
        var runner = ServiceProvider.GetRequiredService<IAgentTurnRunner>();
        await foreach (var item in runner.RunAsync(new(this.GetPrimaryKeyString(), Guid.NewGuid().ToString("N"),
            this.GetPrimaryKeyString(), [], request.Message, request.Model, request.System), CancellationToken.None).ConfigureAwait(true))
        {
            if (item is AgentTurnEvent.Text delta) { text += delta.Content; }
            if (item is AgentTurnEvent.Failed failure) { throw new InvalidOperationException(failure.Message); }
        }

        await PublishAsync(new AgentReplied(this.GetPrimaryKeyString(), text, DateTimeOffset.UtcNow)).ConfigureAwait(true);
        return new AgentReply(text);
    }
}
