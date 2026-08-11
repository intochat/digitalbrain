using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

public sealed partial class ExecutionNeuron
{
    public async Task HandleAsync(UserActionRequired control, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(control);
        cancellationToken.ThrowIfCancellationRequested();

        if (control.Execution != Id)
        {
            return;
        }

        var data = Load();

        if (control.Attempt != data.ActiveAttempt
            || data.State is not (ExecutionState.Pending or ExecutionState.Running or ExecutionState.Waiting))
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

        if (data.State == ExecutionState.Waiting
            && data.Blocker is UserActionPending existing)
        {
            if (existing.ActionEpoch == control.ActionEpoch
                && existing.ActionReference == control.ActionReference
                && existing.Module == control.Module
                && existing.ParkRevision == control.ParkRevision
                && existing.Completer == control.Completer)
            {
                await SendParkReadyAsync(control).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
                return;
            }

            return;
        }

        if (data.PendingDispatch is ContinueWorkerDispatch)
        {
            return;
        }

        data.State = ExecutionState.Waiting;
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

        RequireUserActionAuthority(
            data,
            command.ActionReference,
            command.ActionEpoch,
            command.ExpectedParkRevision);

        data.Revision++;
        data.State = ExecutionState.Running;
        data.Blocker = null;
        data.PendingDispatch = new ContinueWorkerDispatch(Cursor(data));

        var snapshot = Snapshot(data);
        RememberReceipt(data, command.CommandId, snapshot);

        StageForTurn(data);
        await RegisterDispatchReminderAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

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

        data.State = ExecutionState.Failed;
        data.ActiveAttempt = null;
        data.Blocker = null;
        data.Result = null;
        data.Failure = new UserActionDenied(pending.ModuleId);
        data.Evidence = [];
        data.PendingDispatch = null;

        var snapshot = Snapshot(data);
        RememberReceipt(data, command.CommandId, snapshot);

        StageForTurn(data);
        await UnregisterReminderAsync(RetryReminderName).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await UnregisterReminderAsync(DispatchReminderName).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private UserActionPending RequireUserActionAuthority(
        ExecutionData data,
        ProtectedPayloadReference actionReference,
        Guid actionEpoch,
        long expectedParkRevision)
    {
        if (data.State != ExecutionState.Waiting || data.Blocker is not UserActionPending pending)
        {
            throw new NeuronAuthorizationException(
                $"Execution '{Id}' is not waiting on a module user action.");
        }

        if (data.ActiveAttempt is null)
        {
            throw new NeuronAuthorizationException(
                $"Execution '{Id}' has no active attempt bound to the user action.");
        }

        if (pending.ParkRevision != expectedParkRevision)
        {
            throw new NeuronAuthorizationException(
                $"Execution '{Id}' is parked at revision {pending.ParkRevision}, not expected park revision {expectedParkRevision}.");
        }

        if (pending.ActionEpoch != actionEpoch)
        {
            throw new NeuronAuthorizationException(
                $"Execution '{Id}' rejected an action epoch that does not match the parked user action.");
        }

        if (pending.ActionReference != actionReference)
        {
            throw new NeuronAuthorizationException(
                $"Execution '{Id}' rejected an action reference that does not match the parked user action.");
        }

        if (pending.ExpiresAt <= TimeProvider.GetUtcNow())
        {
            throw new NeuronAuthorizationException(
                $"Execution '{Id}' rejected an expired user-action reference.");
        }

        return pending;
    }

    private Task<SynapseDelivery> SendParkReadyAsync(UserActionRequired control)
        => SendAsync(
            control.Completer,
            new UserActionParkReady(
                control.Execution,
                control.Attempt,
                control.Module,
                control.ModuleId,
                control.ActionReference,
                control.ActionEpoch,
                control.ParkRevision,
                control.ExpiresAt,
                control.Completer));
}
