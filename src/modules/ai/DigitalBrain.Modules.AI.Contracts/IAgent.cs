using DigitalBrain.Abstractions;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

[ClientEntryPoint]
public partial interface IAgent : INeuron
{
    [Alias(nameof(Respond))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task<ChatResponse> Respond(IReadOnlyList<ChatMessage> messages);

    // Default body, not just a signature: Concurrent and GroupChat implement IAgent directly
    // (not through the Agent base class) and do not stream yet. This keeps them on their
    // existing Respond path until their own streaming task gives them a real one.
    [Alias(nameof(RespondStreaming))]
    IAsyncEnumerable<ChatResponseUpdate> RespondStreaming(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        return RespondAsUpdatesAsync();

        async IAsyncEnumerable<ChatResponseUpdate> RespondAsUpdatesAsync()
        {
            var response = await Respond(messages).WaitAsync(cancellationToken).ConfigureAwait(false);
            foreach (var update in response.ToChatResponseUpdates())
            {
                yield return update;
            }
        }
    }
}
