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

        if (!await TrySendPendingDispatchAsync(data, pending))
        {
            return;
        }

        var current = Load();

        if (current.PendingDispatch != pending)
        {
            return;
        }

        // Ownership transfer: once Task durably stages Task→relay, PendingDispatch clears and the
        // dispatch reminder unregisters. Downstream relay→Worker delivery is owned by the durable
        // outbox on the relay activation.
        current.PendingDispatch = null;
        await SaveAsync(current);
        await UnregisterReminderAsync(DispatchReminderName);
    }

    // Turn-atomic path for Complete: buffers Task→relay into the outer turn outbox;
    // PendingDispatch clears only through turn staging (no mid-turn journal write); dispatch
    // reminder stays registered until a later reminder observes no pending and unregisters.
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A pending dispatch remains for reminder-driven redelivery after any staging failure; turn rollback restores staged state.")]
    private async Task StagePendingDispatchForTurnAsync()
    {
        var data = LoadIfStarted();

        if (data is null)
        {
            return;
        }

        var pending = data.PendingDispatch;

        if (pending is null)
        {
            return;
        }

        Synapse envelope = BuildPendingDispatchEnvelope(data, pending);
        var relay = NewWorkerDispatchRelayId();

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
        StageForTurn(current);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A durable pending dispatch remains registered for reminder-driven redelivery after any staging failure.")]
    private async Task<bool> TrySendPendingDispatchAsync(TaskData data, PendingWorkerDispatch pending)
    {
        Synapse envelope = BuildPendingDispatchEnvelope(data, pending);
        var relay = NewWorkerDispatchRelayId();

        try
        {
            await SendAsync(relay, envelope);
            return true;
        }
        catch (Exception)
        {
            await RegisterDispatchReminderAsync();
            return false;
        }
    }

    private static Synapse BuildPendingDispatchEnvelope(TaskData data, PendingWorkerDispatch pending)
        => pending switch
        {
            AcceptWorkerDispatch accept => new RelayWorkerAccept(data.Worker, accept.Request),
            ContinueWorkerDispatch continuation => new RelayWorkerContinue(data.Worker, continuation.Cursor),
            CancelWorkerDispatch cancellation => new RelayWorkerCancel(data.Worker, cancellation.Cursor),
            _ => throw new InvalidOperationException(
                $"Unsupported pending Worker dispatch '{pending.GetType().Name}'."),
        };

    private NeuronId NewWorkerDispatchRelayId()
        => new(
            WorkerDispatchRelay.GrainTypeName,
            Id.Owner,
            Guid.NewGuid().ToString("N"));
}
