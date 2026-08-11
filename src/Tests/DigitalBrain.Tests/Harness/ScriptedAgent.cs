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

    private static readonly ConcurrentDictionary<string, TaskCompletionSource> Holds =
        new(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<string, TaskCompletionSource> EnteredHolds =
        new(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<string, byte> CancelledHolds =
        new(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<string, int> AcceptCounts =
        new(StringComparer.Ordinal);

    internal static void ConfigureHold(string agentName)
    {
        Holds[agentName] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EnteredHolds[agentName] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancelledHolds.TryRemove(agentName, out _);
        AcceptCounts[agentName] = 0;
    }

    internal static void ReleaseHold(string agentName)
    {
        if (Holds.TryGetValue(agentName, out var hold))
        {
            hold.TrySetResult();
        }
    }

    internal static void ClearHold(string agentName)
    {
        Holds.TryRemove(agentName, out _);
        EnteredHolds.TryRemove(agentName, out _);
        CancelledHolds.TryRemove(agentName, out _);
        AcceptCounts.TryRemove(agentName, out _);
    }

    internal static bool WasCancelled(string agentName)
        => CancelledHolds.ContainsKey(agentName);

    internal static int AcceptCount(string agentName)
        => AcceptCounts.TryGetValue(agentName, out var count) ? count : 0;

    internal static async Task WaitUntilHeldAsync(string agentName, TimeSpan patience)
    {
        if (!EnteredHolds.TryGetValue(agentName, out var entered))
        {
            throw new InvalidOperationException($"No hold configured for '{agentName}'.");
        }

        using var timeout = new CancellationTokenSource(patience);
        await entered.Task.WaitAsync(timeout.Token)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public Task<ChatResponse> Respond(IReadOnlyList<ChatMessage> messages)
        => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Reply())));

    public async IAsyncEnumerable<ChatResponseUpdate> RespondStreaming(
        IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AcceptCounts.AddOrUpdate(Id.Name, 1, static (_, current) => current + 1);
        var ambient = VerifiedActor.Current;
        ObservedVerifiedActors[Id.Name] = ambient;
        ObservedVerifiedActorTurns
            .GetOrAdd(Id.Name, static _ => [])
            .Add(ambient);

        if (Holds.TryGetValue(Id.Name, out var hold))
        {
            if (EnteredHolds.TryGetValue(Id.Name, out var entered))
            {
                entered.TrySetResult();
            }

            try
            {
                await hold.Task.WaitAsync(cancellationToken)
                    .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            }
            catch (OperationCanceledException)
            {
                CancelledHolds[Id.Name] = 0;
                throw;
            }
        }

        yield return new ChatResponseUpdate(ChatRole.Assistant, Reply());
    }

    private string Reply() => $"scripted:{Id.Name}";
}
