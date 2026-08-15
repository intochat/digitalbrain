using DigitalBrain.Abstractions;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

[ClientEntryPoint]
[Alias("DigitalBrain.AI.IAgent")]
public interface IAgent : INeuron
{
    [Alias(nameof(Respond))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task<ChatResponse> Respond(IReadOnlyList<ChatMessage> messages);

    [Alias(nameof(RespondStreaming))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    IAsyncEnumerable<ChatResponseUpdate> RespondStreaming(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default);
}
