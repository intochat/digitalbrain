using Microsoft.Extensions.AI;

namespace DigitalBrain.Modules.AI;

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