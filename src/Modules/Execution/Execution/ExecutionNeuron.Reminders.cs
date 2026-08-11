namespace DigitalBrain.Execution;

public sealed partial class ExecutionNeuron
{
    async Task IRemindable.ReceiveReminder(string reminderName, TickStatus status)
    {
        if (string.Equals(reminderName, DispatchReminderName, StringComparison.Ordinal))
        {
            await TryDispatchPendingAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
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
