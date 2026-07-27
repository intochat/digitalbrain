
namespace DigitalBrain.Tasks;

internal sealed partial class TaskNeuron
{
    private Task<Orleans.Runtime.IGrainReminder> RegisterDispatchReminderAsync()
        => this.RegisterOrUpdateReminder(
            DispatchReminderName,
            TimeSpan.FromSeconds(1),
            ReminderPeriod);

    private async Task UnregisterReminderAsync(string reminderName)
    {
        if (await this.GetReminder(reminderName) is { } reminder)
        {
            await this.UnregisterReminder(reminder);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A durable pending dispatch remains registered for reminder-driven redelivery after any Worker failure.")]
    private async Task TryDispatchPendingAsync()
    {
        var data = LoadIfStarted();

        if (data is null)
        {
            await UnregisterReminderAsync(DispatchReminderName);
            return;
        }

        var pending = data.PendingDispatch;

        if (pending is null)
        {
            await UnregisterReminderAsync(DispatchReminderName);
            return;
        }

        if (pending is not (AcceptWorkerDispatch or ContinueWorkerDispatch or CancelWorkerDispatch))
        {
            throw new InvalidOperationException(
                $"Task '{Id}' has an unsupported pending Worker dispatch '{pending.GetType().Name}'.");
        }

        try
        {
            var worker = Worker(data);

            switch (pending)
            {
                case AcceptWorkerDispatch accept:
                    await worker.Accept(accept.Request);
                    break;

                case ContinueWorkerDispatch continuation:
                    await worker.Continue(continuation.Cursor);
                    break;

                case CancelWorkerDispatch cancellation:
                    await worker.Cancel(cancellation.Cursor);
                    break;
            }
        }
        catch (Exception)
        {
            await RegisterDispatchReminderAsync();
            return;
        }

        var current = Load();

        if (current.PendingDispatch != pending)
        {
            return;
        }

        current.PendingDispatch = null;
        await SaveAsync(current);
        await UnregisterReminderAsync(DispatchReminderName);
    }

    private IWorker Worker(TaskData data)
        => GrainFactory.GetGrain<IWorker>(data.Worker.ToGrainId());
}
