using DigitalBrain.Abstractions;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

[ClientEntryPoint]
public partial interface ILLM : INeuron
{
    [Alias(nameof(Respond))]
    Task<ChatResponse> Respond(IReadOnlyList<ChatMessage> messages);

    [Alias(nameof(RespondStreaming))]
    IAsyncEnumerable<ChatResponseUpdate> RespondStreaming(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default);
}
