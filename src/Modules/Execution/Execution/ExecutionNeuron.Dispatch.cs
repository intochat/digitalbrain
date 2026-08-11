using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

public sealed partial class ExecutionNeuron
{
    private Task<IGrainReminder> RegisterDispatchReminderAsync()
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

    private async Task<bool> TrySendPendingDispatchAsync(ExecutionData data, PendingWorkerDispatch pending)
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

    private static Synapse BuildPendingDispatchEnvelope(ExecutionData data, PendingWorkerDispatch pending)
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

    private async Task RecoverAfterActivationAsync()
    {
        var data = LoadIfStarted();
        if (data is null || IsTerminal(data.State))
        {
            return;
        }

        // Only re-arm durable pending work. Do NOT re-dispatch Accept/Fail-on-Running
        // here: Chat (and other origins) re-Read during terminal handling, and a nested
        // Accept→worker→origin.Read deadlocks the origin grain turn.
        if (data.PendingDispatch is not null)
        {
            await RegisterDispatchReminderAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await TryDispatchPendingAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        if (data.State == ExecutionState.Cancelling && data.ActiveAttempt is not null)
        {
            data.PendingDispatch = new CancelWorkerDispatch(Cursor(data));
            await RegisterDispatchReminderAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await SaveAsync(data).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await TryDispatchPendingAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        if (data.State == ExecutionState.Waiting
            && data.Blocker is RetryScheduled
            && data.AttemptCount < data.Policy.MaximumAttempts
            && (data.Policy.Deadline is null || data.Policy.Deadline > DateTimeOffset.UtcNow))
        {
            await this.RegisterOrUpdateReminder(RetryReminderName, data.Policy.RetryDelay, ReminderPeriod)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        // Silo recycle drops in-memory liveness reminders. Re-arm only — never
        // FailAbandoned here (origin re-Read would nest-deadlock the chat turn).
        if (data.State == ExecutionState.Running && data.ActiveAttempt is not null)
        {
            await this.RegisterOrUpdateReminder(
                    DispatchReminderName,
                    ExecutionLiveness.WorkerLeaseTimeout,
                    ReminderPeriod)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }

    }

    // Silo restart / worker death: fail a Running/Cancelling attempt that has no pending dispatch.
    // Invoked from the retry/dispatch reminder path only — never from origin re-Read activation.
    private async Task FailAbandonedRunningIfNeededAsync()
    {
        var data = LoadIfStarted();
        if (data is null
            || data.ActiveAttempt is null
            || data.PendingDispatch is not null
            || data.State is not (ExecutionState.Running or ExecutionState.Cancelling))
        {
            return;
        }

        // Cancelling without a worker ack must not stick forever (PendingDispatch already cleared).
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
            await UnregisterReminderAsync(RetryReminderName).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await UnregisterReminderAsync(DispatchReminderName).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await SaveAsync(data).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await NotifyOriginOfStateAsync(data).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        if (TryMarkDispatchedOperationsUncertain(data, out var uncertainBlocker))
        {
            data.State = ExecutionState.Waiting;
            data.Blocker = new OutcomeUncertain(uncertainBlocker);
            data.PendingDispatch = null;
            await SaveAsync(data).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await EmitAsync(new AttemptOutcomeUncertain(
                Id,
                data.Worker,
                data.ActiveAttempt.Value,
                data.Revision,
                uncertainBlocker)).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await NotifyOriginOfStateAsync(data).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
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
        await UnregisterReminderAsync(RetryReminderName).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await UnregisterReminderAsync(DispatchReminderName).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await SaveAsync(data).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await NotifyOriginOfStateAsync(data).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }
}
