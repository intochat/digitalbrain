using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

internal sealed class ExecutionDispatchQueue(ExecutionRuntime runtime)
{
    internal Task<IGrainReminder> RegisterReminderAsync()
        => runtime.RegisterReminderAsync(
            ExecutionReminders.Dispatch,
            TimeSpan.FromSeconds(1),
            ExecutionReminders.Period);

    internal Task UnregisterReminderAsync(string reminderName)
        => runtime.UnregisterReminderAsync(reminderName);

    internal async Task TryDispatchPendingAsync()
    {
        var data = runtime.LoadIfStarted();

        if (data is null)
        {
            await UnregisterReminderAsync(ExecutionReminders.Dispatch)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        var pending = data.PendingDispatch;

        if (pending is null)
        {
            await UnregisterReminderAsync(ExecutionReminders.Dispatch)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        if (!await TrySendPendingDispatchAsync(data, pending)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext))
        {
            return;
        }

        var current = runtime.Load();

        if (current.PendingDispatch != pending)
        {
            return;
        }

        current.PendingDispatch = null;
        await runtime.SaveAsync(current)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await UnregisterReminderAsync(ExecutionReminders.Dispatch)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    internal async Task StagePendingDispatchForTurnAsync()
    {
        var data = runtime.LoadIfStarted();

        if (data?.PendingDispatch is not { } pending)
        {
            return;
        }

        var envelope = BuildEnvelope(data, pending);
        var relay = NewRelayId();

        try
        {
            await runtime.SendAsync(relay, envelope)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        catch (Exception)
        {
            await RegisterReminderAsync()
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        var current = runtime.Load();

        if (current.PendingDispatch != pending)
        {
            return;
        }

        current.PendingDispatch = null;
        runtime.StageForTurn(current);
    }

    private async Task<bool> TrySendPendingDispatchAsync(
        ExecutionData data,
        PendingWorkerDispatch pending)
    {
        var envelope = BuildEnvelope(data, pending);
        var relay = NewRelayId();

        try
        {
            await runtime.SendAsync(relay, envelope)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return true;
        }
        catch (Exception)
        {
            await RegisterReminderAsync()
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return false;
        }
    }

    private static Synapse BuildEnvelope(ExecutionData data, PendingWorkerDispatch pending)
        => pending switch
        {
            AcceptWorkerDispatch accept => new RelayWorkerAccept(data.Worker, accept.Request),
            ContinueWorkerDispatch continuation =>
                new RelayWorkerContinue(data.Worker, continuation.Cursor),
            CancelWorkerDispatch cancellation =>
                new RelayWorkerCancel(data.Worker, cancellation.Cursor),
            _ => throw new InvalidOperationException(
                $"Unsupported pending Worker dispatch '{pending.GetType().Name}'."),
        };

    private NeuronId NewRelayId()
        => new(
            WorkerDispatchRelay.GrainTypeName,
            runtime.Id.Owner,
            Guid.NewGuid().ToString("N"));
}
