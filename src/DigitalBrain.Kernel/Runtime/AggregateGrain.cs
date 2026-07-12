using DigitalBrain.Core.Runtime;
using DigitalBrain.Core.V2;
using DigitalBrain.Kernel.Runtime;
using Orleans;

namespace DigitalBrain.Kernel;

[GrainType("digitalbrain.v2.aggregate")]
public sealed class AggregateGrain([PersistentState("v2-aggregate", "Default")] IPersistentState<AggregateGrainState> state) : Grain, IAggregateGrain
{
    private bool _poisoned;

    public Task<V2AggregateSnapshot> ReadAsync()
    {
        DemandUsable();
        return Task.FromResult(state.State.Snapshot());
    }

    public async Task<V2CommitResult> CommitAsync(V2CommitRequest request)
    {
        DemandUsable();
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
        DemandUsable();
        var current = state.State.Snapshot();
        if (current.EffectTransitions.Any(x => x.TransitionId == transition.TransitionId)) return;
        await PersistAsync(AggregateRetention.Compact(current with
        {
            EffectTransitions = current.EffectTransitions.Append(transition).ToArray()
        }));
    }

    public async Task<bool> TryAppendEffectTransitionAsync(string effectId, string? expectedTransitionId, EffectTransitionRecord transition)
    {
        DemandUsable();
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
        try
        {
            await PersistedStateReconciliation.WriteWithRollbackAsync(
                state, AggregateGrainState.FromSnapshot(next), SameAggregateState);
        }
        catch (PersistedStateWriteOutcomeUnknownException)
        {
            // The write outcome is genuinely unknown (the write failed and the recovery read also failed) --
            // this activation may be holding an unconfirmed snapshot, so it must not serve another read or
            // accept another commit until Orleans reactivates it fresh from durable storage.
            _poisoned = true;
            throw;
        }
    }

    private void DemandUsable()
    {
        if (_poisoned)
            throw new RuntimeStateIntegrityException("aggregate write outcome for this activation is unknown");
    }

    // Cheap structural proxy rather than a full deep comparison: PersistAsync always constructs a
    // monotonically-appended next state, so matching sequence/count plus the tail identity of each list is
    // enough to tell "this exact write landed" from "it didn't", the same way SameEnvelope does for the
    // immutable encrypted envelope.
    private static bool SameAggregateState(AggregateGrainState first, AggregateGrainState second) =>
        first.CommitSequence == second.CommitSequence &&
        first.Commits.Count == second.Commits.Count &&
        (first.Commits.Count == 0 || first.Commits[^1].CommitId == second.Commits[^1].CommitId) &&
        first.EffectTransitions.Count == second.EffectTransitions.Count &&
        (first.EffectTransitions.Count == 0 ||
         first.EffectTransitions[^1].TransitionId == second.EffectTransitions[^1].TransitionId);
}
