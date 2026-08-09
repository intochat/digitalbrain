using System.Runtime.CompilerServices;
using DigitalBrain.AI;
using DigitalBrain.Core;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Tests.Harness;

[GrainType("scriptedagent")]
internal sealed class ScriptedAgent : Neuron, IAgent
{
    public Task<ChatResponse> Respond(IReadOnlyList<ChatMessage> messages)
        => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Reply())));

    public async IAsyncEnumerable<ChatResponseUpdate> RespondStreaming(
        IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.CompletedTask;
        yield return new ChatResponseUpdate(ChatRole.Assistant, Reply());
    }

    private string Reply() => $"scripted:{Id.Name}";
}
