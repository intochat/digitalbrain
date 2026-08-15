using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

internal sealed class ExecutionCanceller(
    ExecutionRuntime runtime,
    ExecutionDispatcher dispatcher)
{
    internal async Task<ExecutionSnapshot> CancelAsync(
        CommandId commandId,
        long? expectedRevision)
    {
        var data = runtime.Load();

        if (data.Receipts.TryGetValue(commandId, out var received))
        {
            return received;
        }

        if (expectedRevision is null)
        {
            throw new NeuronAuthorizationException("Cancel requires ExpectedRevision.");
        }

        if (expectedRevision.Value != data.Revision)
        {
            throw new NeuronAuthorizationException(
                $"Execution '{runtime.Id}' is at revision {data.Revision}, not expected revision "
                + $"{expectedRevision.Value}.");
        }

        if (ExecutionModel.IsTerminal(data.State))
        {
            var terminal = ExecutionModel.Snapshot(data);
            ExecutionModel.RememberReceipt(data, commandId, terminal);
            await runtime.SaveAsync(data)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return terminal;
        }

        if (data.ActiveAttempt is null)
        {
            data.State = ExecutionState.Cancelled;
            data.Blocker = null;
            data.PendingDispatch = null;

            var cancelled = ExecutionModel.Snapshot(data);
            ExecutionModel.RememberReceipt(data, commandId, cancelled);
            await runtime.SaveAsync(data)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await dispatcher.UnregisterReminderAsync(ExecutionReminders.Retry)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await dispatcher.UnregisterReminderAsync(ExecutionReminders.Dispatch)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await runtime.NotifyOriginOfStateAsync(data)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return cancelled;
        }

        if (ExecutionOperationLedger.TryMarkDispatchedUncertain(
            runtime.Id,
            data,
            out var uncertainBlocker))
        {
            data.State = ExecutionState.Waiting;
            data.Blocker = new OutcomeUncertain(uncertainBlocker);
            data.PendingDispatch = new CancelWorkerDispatch(
                ExecutionModel.Cursor(runtime.Id, data));

            var uncertainSnapshot = ExecutionModel.Snapshot(data);
            ExecutionModel.RememberReceipt(data, commandId, uncertainSnapshot);
            await dispatcher.RegisterDispatchReminderAsync()
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await runtime.SaveAsync(data)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await runtime.EmitAsync(new AttemptOutcomeUncertain(
                    runtime.Id,
                    data.Worker,
                    data.ActiveAttempt.Value,
                    data.Revision,
                    uncertainBlocker))
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await dispatcher.UnregisterReminderAsync(ExecutionReminders.Retry)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await runtime.NotifyOriginOfStateAsync(data)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await dispatcher.TryDispatchPendingAsync()
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return uncertainSnapshot;
        }

        data.State = ExecutionState.Cancelling;
        data.Blocker = null;
        data.PendingDispatch = new CancelWorkerDispatch(
            ExecutionModel.Cursor(runtime.Id, data));
        runtime.DelayDeactivation(TimeSpan.FromHours(2));

        var snapshot = ExecutionModel.Snapshot(data);
        ExecutionModel.RememberReceipt(data, commandId, snapshot);

        await dispatcher.RegisterDispatchReminderAsync()
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await runtime.SaveAsync(data)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await dispatcher.TryDispatchPendingAsync()
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        var afterDispatch = runtime.LoadIfStarted();
        if (afterDispatch is { State: ExecutionState.Cancelling, PendingDispatch: null })
        {
            await runtime.RegisterReminderAsync(
                    ExecutionReminders.Dispatch,
                    ExecutionLiveness.WorkerLeaseTimeout,
                    ExecutionReminders.Period)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }

        return snapshot;
    }
}
