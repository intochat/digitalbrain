using DigitalBrain.Contracts;

namespace DigitalBrain.AI.Agents;

[Alias("agent")]
public interface IAgent : INeuron
{
    Task<AgentReply> Ask(AgentRequest request);
}