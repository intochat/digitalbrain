using DigitalBrain.Abstractions;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

[Alias("ai.agent")]
public partial interface IAgent : INeuron
{
    [Alias(nameof(Respond))]
    Task<ChatResponse> Respond(IReadOnlyList<ChatMessage> messages);
}
