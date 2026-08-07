using DigitalBrain.Abstractions;

namespace DigitalBrain.Tasks;

internal sealed partial class TaskNeuron
{
    public async Task HandleAsync(UserActionRequired control, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(control);
        cancellationToken.ThrowIfCancellationRequested();

        if (control.Task != Id)
        {
            return;
        }

        var data = Load();

        if (control.Attempt != data.ActiveAttempt
            || data.State is not (TaskState.Pending or TaskState.Running or TaskState.Waiting))
        {
            return;
        }

        if (control.ExpiresAt <= TimeProvider.GetUtcNow())
        {
            return;
        }

        if (control.ParkRevision != data.Revision)
        {
            return;
        }

        if (data.State == TaskState.Waiting
            && data.Blocker is UserActionPending existing)
        {
            if (existing.ActionEpoch == control.ActionEpoch
                && existing.ActionReference == control.ActionReference
                && existing.Module == control.Module
                && existing.ParkRevision == control.ParkRevision
                && existing.Completer == control.Completer)
            {
                // Same durable park: re-emit park-ready so a lost completer rendezvous can recover
                // without ordinary outbox horizon dependence on the original signal.
                await SendParkReadyAsync(control).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
                return;
            }

            return;
        }

        if (data.PendingDispatch is ContinueWorkerDispatch)
        {
            return;
        }

        data.State = TaskState.Waiting;
        data.Blocker = new UserActionPending(
            new BlockerId(Guid.NewGuid()),
            control.Module,
            control.ModuleId,
            control.DisplayText,
            control.ActionReference,
            control.ActionEpoch,
            control.ParkRevision,
            control.ExpiresAt,
            control.Completer);
        data.PendingDispatch = null;

        StageForTurn(data);
        await SendParkReadyAsync(control).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public async Task HandleAsync(CompleteUserAction command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        Validate(command.CommandId);

        var data = Load();

        if (data.Receipts.TryGetValue(command.CommandId, out _))
        {
            return;
        }

        var pending = RequireUserActionAuthority(
            data,
            command.ActionReference,
            command.ActionEpoch,
            command.ExpectedParkRevision);

        data.Revision++;
        data.State = TaskState.Running;
        data.Blocker = null;
        data.PendingDispatch = new ContinueWorkerDispatch(Cursor(data));

        var snapshot = Snapshot(data);
        data.Receipts.Add(command.CommandId, snapshot);

        StageForTurn(data);
        await RegisterDispatchReminderAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        // Turn-atomic: buffer Task→relay and clear PendingDispatch in staged memory only.
        // A mid-turn durable ownership transfer would journal-commit before the outer turn.
        await StagePendingDispatchForTurnAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public async Task HandleAsync(DenyUserAction command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        Validate(command.CommandId);

        var data = Load();

        if (data.Receipts.TryGetValue(command.CommandId, out _))
        {
            return;
        }

        var pending = RequireUserActionAuthority(
            data,
            command.ActionReference,
            command.ActionEpoch,
            command.ExpectedParkRevision);

        data.State = TaskState.Failed;
        data.ActiveAttempt = null;
        data.Blocker = null;
        data.Result = null;
        data.Failure = new UserActionDenied(pending.ModuleId);
        data.Evidence = [];
        data.PendingDispatch = null;

        var snapshot = Snapshot(data);
        data.Receipts.Add(command.CommandId, snapshot);

        // Stage only; reminder cleanup then the outer durable turn commits state/receipt once.
        // Mid-handler durable writes are forbidden — cleanup failure throws and rolls back to Waiting.
        StageForTurn(data);
        await UnregisterReminderAsync(RetryReminderName).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await UnregisterReminderAsync(DispatchReminderName).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private UserActionPending RequireUserActionAuthority(
        TaskData data,
        ProtectedPayloadReference actionReference,
        Guid actionEpoch,
        long expectedParkRevision)
    {
        if (data.State != TaskState.Waiting || data.Blocker is not UserActionPending pending)
        {
            throw new NeuronAuthorizationException(
                $"Task '{Id}' is not waiting on a module user action.");
        }

        if (data.ActiveAttempt is null)
        {
            throw new NeuronAuthorizationException(
                $"Task '{Id}' has no active attempt bound to the user action.");
        }

        if (pending.ParkRevision != expectedParkRevision)
        {
            throw new NeuronAuthorizationException(
                $"Task '{Id}' is parked at revision {pending.ParkRevision}, not expected park revision {expectedParkRevision}.");
        }

        if (pending.ActionEpoch != actionEpoch)
        {
            throw new NeuronAuthorizationException(
                $"Task '{Id}' rejected an action epoch that does not match the parked user action.");
        }

        if (pending.ActionReference != actionReference)
        {
            throw new NeuronAuthorizationException(
                $"Task '{Id}' rejected an action reference that does not match the parked user action.");
        }

        if (pending.ExpiresAt <= TimeProvider.GetUtcNow())
        {
            throw new NeuronAuthorizationException(
                $"Task '{Id}' rejected an expired user-action reference.");
        }

        return pending;
    }

    private Task<SynapseDelivery> SendParkReadyAsync(UserActionRequired control)
        => SendAsync(
            control.Completer,
            new UserActionParkReady(
                control.Task,
                control.Attempt,
                control.Module,
                control.ModuleId,
                control.ActionReference,
                control.ActionEpoch,
                control.ParkRevision,
                control.ExpiresAt,
                control.Completer));
}
