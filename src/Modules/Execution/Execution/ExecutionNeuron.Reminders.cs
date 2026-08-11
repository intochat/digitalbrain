namespace DigitalBrain.Execution;

public sealed partial class ExecutionNeuron
{
    async Task IRemindable.ReceiveReminder(string reminderName, TickStatus status)
    {
        if (string.Equals(reminderName, DispatchReminderName, StringComparison.Ordinal))
        {
            await TryDispatchPendingAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await FailAbandonedRunningIfNeededAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        if (string.Equals(reminderName, RetryReminderName, StringComparison.Ordinal))
        {
            await FailAbandonedRunningIfNeededAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }

        if (!string.Equals(reminderName, RetryReminderName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Execution neuron '{Id}' does not own reminder '{reminderName}'.");
        }

        var data = LoadIfStarted();

        if (data is null)
        {
            await UnregisterReminderAsync(RetryReminderName).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        // OutcomeUncertain never auto-retries — only RetryScheduled may advance.
        if (data.State != ExecutionState.Waiting
            || data.Blocker is not RetryScheduled
            || data.AttemptCount >= data.Policy.MaximumAttempts
            || (data.Policy.Deadline is not null && data.Policy.Deadline <= DateTimeOffset.UtcNow))
        {
            await UnregisterReminderAsync(RetryReminderName).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        // Defense: never redispatch while any operation is started without a terminal outcome.
        if (TryMarkDispatchedOperationsUncertain(data, out var uncertainBlocker))
        {
            data.State = ExecutionState.Waiting;
            data.Blocker = new OutcomeUncertain(uncertainBlocker);
            data.PendingDispatch = null;
            await UnregisterReminderAsync(RetryReminderName).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await SaveAsync(data).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            if (data.ActiveAttempt is { } attempt)
            {
                await EmitAsync(new AttemptOutcomeUncertain(
                    Id,
                    data.Worker,
                    attempt,
                    data.Revision,
                    uncertainBlocker)).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            }

            return;
        }

        data.Revision++;
        data.State = ExecutionState.Pending;
        data.ActiveAttempt = new AttemptId(Guid.NewGuid());
        data.Blocker = null;
        data.AttemptCount++;
        data.PendingDispatch = new AcceptWorkerDispatch(Request(data));

        await RegisterDispatchReminderAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await SaveAsync(data).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await UnregisterReminderAsync(RetryReminderName).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await TryDispatchPendingAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }
}
