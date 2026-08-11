using DigitalBrain.Abstractions;
using DigitalBrain.Core;

namespace DigitalBrain.Execution;

internal sealed class ExecutionUserActionHandler(
    ExecutionRuntime runtime,
    ExecutionDispatcher dispatcher)
{
    internal async Task HandleRequiredAsync(
        UserActionRequired control,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(control);
        cancellationToken.ThrowIfCancellationRequested();

        if (control.Execution != runtime.Id)
        {
            return;
        }

        var data = runtime.Load();

        if (control.Attempt != data.ActiveAttempt
            || data.State is not (ExecutionState.Pending or ExecutionState.Running or ExecutionState.Waiting))
        {
            return;
        }

        if (control.ExpiresAt <= runtime.TimeProvider.GetUtcNow())
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
                await SendParkReadyAsync(control)
                    .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
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

        runtime.StageForTurn(data);
        await SendParkReadyAsync(control)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    internal async Task CompleteAsync(
        CompleteUserAction command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        ExecutionModel.ValidateCommandId(command.CommandId);

        var data = runtime.Load();

        if (data.Receipts.TryGetValue(command.CommandId, out _))
        {
            return;
        }

        RequireAuthority(
            data,
            command.ActionReference,
            command.ActionEpoch,
            command.ExpectedParkRevision);

        data.Revision++;
        data.State = ExecutionState.Running;
        data.Blocker = null;
        data.PendingDispatch = new ContinueWorkerDispatch(
            ExecutionModel.Cursor(runtime.Id, data));

        var snapshot = ExecutionModel.Snapshot(data);
        ExecutionModel.RememberReceipt(data, command.CommandId, snapshot);

        runtime.StageForTurn(data);
        await dispatcher.RegisterDispatchReminderAsync()
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        await dispatcher.StagePendingDispatchForTurnAsync()
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    internal async Task DenyAsync(
        DenyUserAction command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        ExecutionModel.ValidateCommandId(command.CommandId);

        var data = runtime.Load();

        if (data.Receipts.TryGetValue(command.CommandId, out _))
        {
            return;
        }

        var pending = RequireAuthority(
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

        var snapshot = ExecutionModel.Snapshot(data);
        ExecutionModel.RememberReceipt(data, command.CommandId, snapshot);

        runtime.StageForTurn(data);
        await dispatcher.UnregisterReminderAsync(ExecutionReminders.Retry)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await dispatcher.UnregisterReminderAsync(ExecutionReminders.Dispatch)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private UserActionPending RequireAuthority(
        ExecutionData data,
        ProtectedPayloadReference actionReference,
        Guid actionEpoch,
        long expectedParkRevision)
    {
        if (data.State != ExecutionState.Waiting || data.Blocker is not UserActionPending pending)
        {
            throw new NeuronAuthorizationException(
                $"Execution '{runtime.Id}' is not waiting on a module user action.");
        }

        if (data.ActiveAttempt is null)
        {
            throw new NeuronAuthorizationException(
                $"Execution '{runtime.Id}' has no active attempt bound to the user action.");
        }

        if (pending.ParkRevision != expectedParkRevision)
        {
            throw new NeuronAuthorizationException(
                $"Execution '{runtime.Id}' is parked at revision {pending.ParkRevision}, not "
                + $"expected park revision {expectedParkRevision}.");
        }

        if (pending.ActionEpoch != actionEpoch)
        {
            throw new NeuronAuthorizationException(
                $"Execution '{runtime.Id}' rejected an action epoch that does not match the "
                + "parked user action.");
        }

        if (pending.ActionReference != actionReference)
        {
            throw new NeuronAuthorizationException(
                $"Execution '{runtime.Id}' rejected an action reference that does not match "
                + "the parked user action.");
        }

        if (pending.ExpiresAt <= runtime.TimeProvider.GetUtcNow())
        {
            throw new NeuronAuthorizationException(
                $"Execution '{runtime.Id}' rejected an expired user-action reference.");
        }

        return pending;
    }

    private Task<SynapseDelivery> SendParkReadyAsync(UserActionRequired control)
        => runtime.SendAsync(
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
