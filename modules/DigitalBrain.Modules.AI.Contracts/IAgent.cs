using DigitalBrain.Abstractions;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

[Alias("ai.agent")]
public interface IAgent : INeuron
{
    [Alias("Ask")]
    Task<ChatResponse> RespondAsync(IReadOnlyList<ChatMessage> messages);
}
