using DigitalBrain.Abstractions;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

[ClientEntryPoint]
public partial interface IAgent : INeuron
{
    [Alias(nameof(Respond))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task<ChatResponse> Respond(IReadOnlyList<ChatMessage> messages);
}
