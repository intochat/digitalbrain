using Brain.Abstractions.Activities;
using Brain.Abstractions.Identity;

namespace Brain.Core.Activities;

// The proof uses a rehydratable state store rather than a distributed runtime. A later host can
// bind this lifecycle owner to Orleans persistence without changing the activity projection API.
internal sealed class BrainActivityGrain
{
    private readonly IActivityStore _store;
    private readonly BrainActivityId _activity;

    internal BrainActivityGrain(IActivityStore store, BrainActivityId activity)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _activity = activity;
    }

    internal void Accept(BrainActivityState state) => _store.CreateAccepted(state);

    internal void MarkRunning()
    {
        var state = Require(ActivityStatus.Accepted);
        _store.Save(state with { Status = ActivityStatus.Running });
    }

    internal void Complete(ActivityResultReference result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var state = Require(ActivityStatus.Running);
        _store.Save(state with { Status = ActivityStatus.Completed, Result = result });
    }

    internal void Refuse(ActivityProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        var state = Require(ActivityStatus.Accepted);
        _store.Save(state with { Status = ActivityStatus.Refused, Problem = problem });
    }

    private BrainActivityState Require(ActivityStatus status)
    {
        var state = _store.Get(_activity);
        if (state.Status != status)
        {
            throw new InvalidOperationException(
                $"Activity '{_activity}' must be '{status}' but is '{state.Status}'.");
        }

        return state;
    }
}
