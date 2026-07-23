using DigitalBrain.Abstractions;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

[Alias("ai.llm")]
[ClientEntryPoint]
public interface ILLM : INeuron
{
    [Alias("Ask")]
    Task<ChatResponse> RespondAsync(IReadOnlyList<ChatMessage> messages);
}

