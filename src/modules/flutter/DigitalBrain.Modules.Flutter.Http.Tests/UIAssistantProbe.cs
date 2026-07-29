using System.Runtime.CompilerServices;
using DigitalBrain.AI;
using DigitalBrain.Kernel;
using Microsoft.Extensions.AI;

namespace DigitalBrain.UI.Tests;

[GrainType("assistant")]
public sealed class UIAssistantProbe : Neuron, IAssistant
{
    internal const string Answer = "the edge probe answered";

    public async IAsyncEnumerable<ChatResponseUpdate> RespondStreaming(
        IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        cancellationToken.ThrowIfCancellationRequested();

        yield return new ChatResponseUpdate(ChatRole.Assistant, Answer);

        await Task.CompletedTask;
    }

    public Task<ChatResponse> Respond(IReadOnlyList<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        return RespondStreaming(messages).ToChatResponseAsync();
    }
}
