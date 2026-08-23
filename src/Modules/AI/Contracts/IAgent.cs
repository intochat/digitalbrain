using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;
using Microsoft.Extensions.AI;

using DigitalBrain.Abstractions.Neurons;
namespace DigitalBrain.AI;

[Alias(nameof(IAgent))]
public interface IAgent : INeuron
{
    [Alias(nameof(Respond))]
    Task<ChatResponse> Respond(IReadOnlyList<ChatMessage> messages);

    [Alias(nameof(RespondStreaming))]
    IAsyncEnumerable<ChatResponseUpdate> RespondStreaming(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default);
}