using DigitalBrain.Tasks.Dispatch;

namespace DigitalBrain.Tasks;

internal sealed partial class TaskNeuron
{
    async Task IRemindable.ReceiveReminder(string reminderName, TickStatus status)
    {
        if (string.Equals(reminderName, DispatchReminderName, StringComparison.Ordinal))
        {
            await TryDispatchPendingAsync();
            return;
        }

        if (!string.Equals(reminderName, RetryReminderName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Task neuron '{Id}' does not own reminder '{reminderName}'.");
        }

        var data = LoadIfStarted();

        if (data is null)
        {
            await UnregisterReminderAsync(RetryReminderName);
            return;
        }

        if (data.State != TaskState.Waiting
            || data.Blocker is not RetryScheduled
            || data.AttemptCount >= data.Policy.MaximumAttempts
            || (data.Policy.Deadline is not null && data.Policy.Deadline <= DateTimeOffset.UtcNow))
        {
            await UnregisterReminderAsync(RetryReminderName);
            return;
        }

        data.Revision++;
        data.State = TaskState.Pending;
        data.ActiveAttempt = new AttemptId(Guid.NewGuid());
        data.Blocker = null;
        data.AttemptCount++;
        data.PendingDispatch = new AcceptWorkerDispatch(Request(data));

        await RegisterDispatchReminderAsync();
        await SaveAsync(data);
        await UnregisterReminderAsync(RetryReminderName);
        await TryDispatchPendingAsync();
    }
}
