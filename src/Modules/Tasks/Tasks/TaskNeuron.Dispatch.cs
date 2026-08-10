using DigitalBrain.Abstractions;

namespace DigitalBrain.Tasks;

internal sealed partial class TaskNeuron
{
    private Task<Orleans.Runtime.IGrainReminder> RegisterDispatchReminderAsync()
        => this.RegisterOrUpdateReminder(DispatchReminderName, TimeSpan.FromSeconds(1), ReminderPeriod);

    private async Task UnregisterReminderAsync(string reminderName)
    {
        if (await this.GetReminder(reminderName).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext) is { } reminder)
        {
            await this.UnregisterReminder(reminder).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
    }
    private async Task TryDispatchPendingAsync()
    {
        var data = LoadIfStarted();

        if (data is null)
        {
            await UnregisterReminderAsync(DispatchReminderName).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        var pending = data.PendingDispatch;

        if (pending is null)
        {
            await UnregisterReminderAsync(DispatchReminderName).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        if (!await TrySendPendingDispatchAsync(data, pending).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext))
        {
            return;
        }

        var current = Load();

        if (current.PendingDispatch != pending)
        {
            return;
        }

        current.PendingDispatch = null;
        await SaveAsync(current).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await UnregisterReminderAsync(DispatchReminderName).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

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
            await SendAsync(relay, envelope).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        catch (Exception)
        {
            await RegisterDispatchReminderAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
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
    private async Task<bool> TrySendPendingDispatchAsync(TaskData data, PendingWorkerDispatch pending)
    {
        Synapse envelope = BuildPendingDispatchEnvelope(data, pending);
        var relay = NewWorkerDispatchRelayId();

        try
        {
            await SendAsync(relay, envelope).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return true;
        }
        catch (Exception)
        {
            await RegisterDispatchReminderAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
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
