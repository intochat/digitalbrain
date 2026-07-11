using DigitalBrain.Core.Runtime;
using DigitalBrain.Core.V2;
using DigitalBrain.Kernel.Runtime;
using Orleans;

namespace DigitalBrain.Kernel;

[GrainType("digitalbrain.v2.aggregate")]
public sealed class AggregateGrain([PersistentState("v2-aggregate", "Default")] IPersistentState<AggregateGrainState> state) : Grain, IAggregateGrain
{
    public Task<V2AggregateSnapshot> ReadAsync() => Task.FromResult(state.State.Snapshot());

    public async Task<V2CommitResult> CommitAsync(V2CommitRequest request)
    {
        var current = state.State.Snapshot();
        var duplicate = current.Inbox.FirstOrDefault(x => x.CommandId == request.CommandId && x.CommitId is not null);
        if (duplicate?.CommitId is not null)
        {
            var existing = current.Commits.Single(x => x.CommitId == duplicate.CommitId);
            return new V2CommitResult(false, true, existing, current);
        }
        if (request.ExpectedCommitSequence != current.CommitSequence)
            throw new InvalidOperationException($"Aggregate commit sequence conflict; expected {current.CommitSequence}, received {request.ExpectedCommitSequence}.");
        var events = request.Events.ToArray();
        var commit = new AggregateCommit(current.CommitSequence + 1, "v2-commit-" + Guid.NewGuid().ToString("N"), events, CommitSeal.Compute(events), request.CommittedAt);
        var next = AggregateRetention.Compact(current with
        {
            CommitSequence = commit.CommitSequence,
            State = request.NewState.Clone(),
            Commits = current.Commits.Append(commit).ToArray(),
            Outbox = current.Outbox.Concat(request.Effects).ToArray(),
            Inbox = current.Inbox.Append(new V2InboxRecord(request.CommandId, commit.CommitId, request.CommittedAt)).ToArray()
        });
        await PersistAsync(next);
        return new V2CommitResult(true, false, commit, next);
    }

    public async Task AppendEffectTransitionAsync(EffectTransitionRecord transition)
    {
        var current = state.State.Snapshot();
        if (current.EffectTransitions.Any(x => x.TransitionId == transition.TransitionId)) return;
        await PersistAsync(AggregateRetention.Compact(current with
        {
            EffectTransitions = current.EffectTransitions.Append(transition).ToArray()
        }));
    }

    public async Task<bool> TryAppendEffectTransitionAsync(string effectId, string? expectedTransitionId, EffectTransitionRecord transition)
    {
        if (!string.Equals(effectId, transition.EffectId, StringComparison.Ordinal))
            throw new ArgumentException("The effect transition does not match the requested effect.", nameof(transition));
        var current = state.State.Snapshot();
        if (current.EffectTransitions.Any(x => x.TransitionId == transition.TransitionId)) return true;
        var latest = current.EffectTransitions.LastOrDefault(x => x.EffectId == effectId);
        if (!string.Equals(latest?.TransitionId, expectedTransitionId, StringComparison.Ordinal)) return false;
        await PersistAsync(AggregateRetention.Compact(current with
        {
            EffectTransitions = current.EffectTransitions.Append(transition).ToArray()
        }));
        return true;
    }

    private async Task PersistAsync(V2AggregateSnapshot next)
    {
        var previous = state.State;
        state.State = AggregateGrainState.FromSnapshot(next);
        try
        {
            await state.WriteStateAsync();
        }
        catch
        {
            state.State = previous;
            throw;
        }
    }
}
