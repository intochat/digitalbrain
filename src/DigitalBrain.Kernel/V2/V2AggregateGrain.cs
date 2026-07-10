using DigitalBrain.Core.V2;
using DigitalBrain.Kernel.V2;
using Orleans;

namespace DigitalBrain.Kernel;

[GrainType("digitalbrain.v2.aggregate")]
public sealed class V2AggregateGrain([PersistentState("v2-aggregate", "Default")] IPersistentState<V2AggregateGrainState> state) : Grain, IV2AggregateGrain
{
    public Task<V2AggregateSnapshot> ReadAsync() => Task.FromResult(state.State.Snapshot());

    public async Task<V2CommitResult> CommitAsync(V2CommitRequest request)
    {
        var current = state.State;
        var duplicate = current.Inbox.FirstOrDefault(x => x.CommandId == request.CommandId && x.CommitId is not null);
        if (duplicate?.CommitId is not null)
        {
            var existing = current.Commits.Single(x => x.CommitId == duplicate.CommitId);
            return new V2CommitResult(false, true, existing, current.Snapshot());
        }
        if (request.ExpectedCommitSequence != current.CommitSequence)
            throw new InvalidOperationException($"V2 commit sequence conflict; expected {current.CommitSequence}, received {request.ExpectedCommitSequence}.");
        var events = request.Events.ToArray();
        var commit = new AggregateCommit(current.CommitSequence + 1, "v2-commit-" + Guid.NewGuid().ToString("N"), events, V2CommitSeal.Compute(events), request.CommittedAt);
        current.CommitSequence = commit.CommitSequence;
        current.State = request.NewState.Clone();
        current.Commits.Add(commit);
        current.Outbox.AddRange(request.Effects);
        current.Inbox.Add(new V2InboxRecord(request.CommandId, commit.CommitId, request.CommittedAt));
        await state.WriteStateAsync();
        return new V2CommitResult(true, false, commit, current.Snapshot());
    }

    public async Task AppendEffectTransitionAsync(EffectTransitionRecord transition)
    {
        if (state.State.EffectTransitions.All(x => x.TransitionId != transition.TransitionId))
        {
            state.State.EffectTransitions.Add(transition);
            await state.WriteStateAsync();
        }
    }
}
