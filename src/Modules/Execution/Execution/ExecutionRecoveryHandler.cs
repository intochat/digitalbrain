using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

internal sealed class ExecutionRecoveryHandler(
    ExecutionRuntime runtime,
    ExecutionDispatchQueue queue)
{
    internal async Task RecoverAfterActivationAsync()
    {
        var data = runtime.LoadIfStarted();
        if (data is null || ExecutionModel.IsTerminal(data.State))
        {
            return;
        }

        if (data.PendingDispatch is not null)
        {
            await queue.RegisterReminderAsync()
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await queue.TryDispatchPendingAsync()
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        if (data.State == ExecutionState.Cancelling && data.ActiveAttempt is not null)
        {
            data.PendingDispatch = new CancelWorkerDispatch(
                ExecutionModel.Cursor(runtime.Id, data));
            await queue.RegisterReminderAsync()
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await runtime.SaveAsync(data)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await queue.TryDispatchPendingAsync()
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        if (data.State == ExecutionState.Waiting
            && data.Blocker is RetryScheduled
            && data.AttemptCount < data.Policy.MaximumAttempts
            && (data.Policy.Deadline is null || data.Policy.Deadline > DateTimeOffset.UtcNow))
        {
            await runtime.RegisterReminderAsync(
                    ExecutionReminders.Retry,
                    data.Policy.RetryDelay,
                    ExecutionReminders.Period)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        if (data.State == ExecutionState.Running && data.ActiveAttempt is not null)
        {
            await runtime.RegisterReminderAsync(
                    ExecutionReminders.Dispatch,
                    ExecutionLiveness.WorkerLeaseTimeout,
                    ExecutionReminders.Period)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
    }

    internal async Task ReceiveReminderAsync(string reminderName)
    {
        if (string.Equals(reminderName, ExecutionReminders.Dispatch, StringComparison.Ordinal))
        {
            await queue.TryDispatchPendingAsync()
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await FailAbandonedRunningIfNeededAsync()
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        if (string.Equals(reminderName, ExecutionReminders.Retry, StringComparison.Ordinal))
        {
            await FailAbandonedRunningIfNeededAsync()
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }

        if (!string.Equals(reminderName, ExecutionReminders.Retry, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Execution neuron '{runtime.Id}' does not own reminder '{reminderName}'.");
        }

        var data = runtime.LoadIfStarted();

        if (data is null)
        {
            await queue.UnregisterReminderAsync(ExecutionReminders.Retry)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        if (data.State != ExecutionState.Waiting
            || data.Blocker is not RetryScheduled
            || data.AttemptCount >= data.Policy.MaximumAttempts
            || (data.Policy.Deadline is not null && data.Policy.Deadline <= DateTimeOffset.UtcNow))
        {
            await queue.UnregisterReminderAsync(ExecutionReminders.Retry)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        if (ExecutionOperationLedger.TryMarkDispatchedUncertain(
            runtime.Id,
            data,
            out var uncertainBlocker))
        {
            data.State = ExecutionState.Waiting;
            data.Blocker = new OutcomeUncertain(uncertainBlocker);
            data.PendingDispatch = null;
            await queue.UnregisterReminderAsync(ExecutionReminders.Retry)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await runtime.SaveAsync(data)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            if (data.ActiveAttempt is { } attempt)
            {
                await runtime.EmitAsync(new AttemptOutcomeUncertain(
                        runtime.Id,
                        data.Worker,
                        attempt,
                        data.Revision,
                        uncertainBlocker))
                    .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            }

            return;
        }

        data.Revision++;
        data.State = ExecutionState.Pending;
        data.ActiveAttempt = new AttemptId(Guid.NewGuid());
        data.Blocker = null;
        data.AttemptCount++;
        data.PendingDispatch = new AcceptWorkerDispatch(
            ExecutionModel.Request(runtime.Id, data));

        await queue.RegisterReminderAsync()
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await runtime.SaveAsync(data)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await queue.UnregisterReminderAsync(ExecutionReminders.Retry)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await queue.TryDispatchPendingAsync()
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private async Task FailAbandonedRunningIfNeededAsync()
    {
        var data = runtime.LoadIfStarted();
        if (data is null
            || data.ActiveAttempt is null
            || data.PendingDispatch is not null
            || data.State is not (ExecutionState.Running or ExecutionState.Cancelling))
        {
            return;
        }

        if (data.State == ExecutionState.Cancelling)
        {
            data.Revision++;
            data.State = ExecutionState.Failed;
            data.Failure = new WorkerAbandoned("worker-abandoned-while-cancelling");
            data.ActiveAttempt = null;
            data.Blocker = null;
            data.Result = null;
            data.Evidence = [];
            data.PendingDispatch = null;
            await queue.UnregisterReminderAsync(ExecutionReminders.Retry)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await queue.UnregisterReminderAsync(ExecutionReminders.Dispatch)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await runtime.SaveAsync(data)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await runtime.NotifyOriginOfStateAsync(data)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        if (ExecutionOperationLedger.TryMarkDispatchedUncertain(
            runtime.Id,
            data,
            out var uncertainBlocker))
        {
            data.State = ExecutionState.Waiting;
            data.Blocker = new OutcomeUncertain(uncertainBlocker);
            data.PendingDispatch = null;
            await runtime.SaveAsync(data)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await runtime.EmitAsync(new AttemptOutcomeUncertain(
                    runtime.Id,
                    data.Worker,
                    data.ActiveAttempt.Value,
                    data.Revision,
                    uncertainBlocker))
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await runtime.NotifyOriginOfStateAsync(data)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        data.Revision++;
        data.State = ExecutionState.Failed;
        data.Failure = new WorkerAbandoned("worker-abandoned");
        data.ActiveAttempt = null;
        data.Blocker = null;
        data.Result = null;
        data.Evidence = [];
        data.PendingDispatch = null;
        await queue.UnregisterReminderAsync(ExecutionReminders.Retry)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await queue.UnregisterReminderAsync(ExecutionReminders.Dispatch)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await runtime.SaveAsync(data)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await runtime.NotifyOriginOfStateAsync(data)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }
}
