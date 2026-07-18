using DigitalBrain.Protocol;

namespace DigitalBrain.Os.Application;

public interface IAgent : INeuron
{
    // Awareness + LLM behavior use AgentAwareness + AgentRequest/AgentResponse synapses (via Emit/Ask/Deliver on the timeline or P2P).
    // Concrete impls (LLMNeuron etc.) wire Microsoft.Extensions.AI.IChatClient (10.6.0) exactly as:
    //   new ChatClientBuilder(impl).UseOpenTelemetry(sourceName: "agent", configure: c => { }).UseFunctionInvocation().Build()
    // (or AsBuilder(client).Use... .Build()). Pair with NeuronTelemetry. Core stays pure; real client + builder live in Kernel or the installing experience.
}