using DigitalBrain.Abstractions;

namespace DigitalBrain.Tasks;

internal sealed partial class TaskNeuron
{
    private Task<Orleans.Runtime.IGrainReminder> RegisterDispatchReminderAsync()
        => this.RegisterOrUpdateReminder(DispatchReminderName, TimeSpan.FromSeconds(1), ReminderPeriod);

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
        Justification = "A durable pending dispatch remains registered for reminder-driven redelivery after any staging failure.")]
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

        Synapse envelope = pending switch
        {
            AcceptWorkerDispatch accept => new RelayWorkerAccept(data.Worker, accept.Request),
            ContinueWorkerDispatch continuation => new RelayWorkerContinue(data.Worker, continuation.Cursor),
            CancelWorkerDispatch cancellation => new RelayWorkerCancel(data.Worker, cancellation.Cursor),
            _ => throw new InvalidOperationException(
                $"Task '{Id}' has an unsupported pending Worker dispatch '{pending.GetType().Name}'."),
        };

        var relay = new NeuronId(
            WorkerDispatchRelay.GrainTypeName,
            Id.Owner,
            Guid.NewGuid().ToString("N"));

        try
        {
            await SendAsync(relay, envelope);
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
}
