using DigitalBrain.Abstractions.Neurons;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

// In-silo return-value surface. Owners and scripts use IAgent + RequestAsync.
// Nested RequestAsync deadlocks the session neuron — ChatTurnWorker uses this instead.
[Alias("agent.runtime")]
public interface IAgentKernel : IGrainWithStringKey
{
    [Alias(nameof(Ask))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task<AgentReply> Ask(AgentRequest request, CancellationToken cancellationToken = default);

    [Alias(nameof(AskStreaming))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    IAsyncEnumerable<ChatResponseUpdate> AskStreaming(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default);

    [Alias(nameof(InvalidateMcpTools))]
    Task InvalidateMcpTools();
}
