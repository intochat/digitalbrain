using Brain.Abstractions.Activities;
using Brain.Abstractions.Context;
using Brain.Abstractions.Identity;

namespace Brain.Core.Activities;

internal sealed class ActivityProjectionService
{
    private readonly IActivityStore _store;

    internal ActivityProjectionService(IActivityStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    internal Task<ActivityView> ObserveAsync(
        BrainActivityId activity,
        WorkspaceContext caller,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        cancellationToken.ThrowIfCancellationRequested();
        var state = _store.Get(activity);
        if (state.Caller.Workspace != caller.Workspace || state.Caller.Principal != caller.Principal)
        {
            throw new UnauthorizedAccessException("The caller cannot observe this activity.");
        }

        return Task.FromResult(new ActivityView(
            state.Activity,
            state.Operation,
            state.Status,
            state.TerminalResultContract,
            state.Progress,
            state.Result,
            state.Problem));
    }
}
