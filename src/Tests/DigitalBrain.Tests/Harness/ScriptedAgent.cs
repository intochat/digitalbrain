using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Core;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Tests.Harness;

[GrainType("scriptedagent")]
internal sealed class ScriptedAgent : Neuron, IAgent
{
    // Test observation: VerifiedActor ambient as seen inside the agent grain turn.
    internal static ConcurrentDictionary<string, ActorContext?> ObservedVerifiedActors { get; } = new(StringComparer.Ordinal);

    // Per-turn samples (concurrent multi-principal pins append; last-write dictionary alone would race).
    internal static ConcurrentDictionary<string, ConcurrentBag<ActorContext?>> ObservedVerifiedActorTurns { get; }
        = new(StringComparer.Ordinal);

    public Task<ChatResponse> Respond(IReadOnlyList<ChatMessage> messages)
        => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Reply())));

    public async IAsyncEnumerable<ChatResponseUpdate> RespondStreaming(
        IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ambient = VerifiedActor.Current;
        ObservedVerifiedActors[Id.Name] = ambient;
        ObservedVerifiedActorTurns
            .GetOrAdd(Id.Name, static _ => [])
            .Add(ambient);
        await Task.CompletedTask;
        yield return new ChatResponseUpdate(ChatRole.Assistant, Reply());
    }

    private string Reply() => $"scripted:{Id.Name}";
}
