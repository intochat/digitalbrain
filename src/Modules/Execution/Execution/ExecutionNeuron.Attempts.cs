namespace DigitalBrain.Execution;

public sealed partial class ExecutionNeuron
{
    public Task HandleAsync(AttemptAccepted fact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fact);

        var data = Load();

        if (!Matches(data, fact) || data.State != ExecutionState.Pending)
        {
            return Task.CompletedTask;
        }

        data.State = ExecutionState.Running;
        AcknowledgePendingDispatch(data, fact);

        Stage(data);
        return Task.CompletedTask;
    }

    public Task HandleAsync(AttemptWaiting fact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fact);

        if (fact.Blocker is null)
        {
            return Task.CompletedTask;
        }

        var data = Load();

        if (!Matches(data, fact)
            || data.State is not (ExecutionState.Pending or ExecutionState.Running or ExecutionState.Waiting)
            || IsOutcomeUncertain(data))
        {
            return Task.CompletedTask;
        }

        data.State = ExecutionState.Waiting;
        data.Blocker = fact.Blocker;
        AcknowledgePendingDispatch(data, fact);

        Stage(data);
        return Task.CompletedTask;
    }

    public async Task HandleAsync(AttemptProgressed fact, CancellationToken cancellationToken)
        => await AdvanceAsync(fact).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

    private async Task AdvanceAsync(AttemptFact fact)
    {
        ArgumentNullException.ThrowIfNull(fact);

        var data = Load();

        if (!Matches(data, fact)
            || data.State is not (ExecutionState.Running or ExecutionState.Waiting)
            || IsOutcomeUncertain(data))
        {
            return;
        }

        data.Revision++;
        data.State = ExecutionState.Running;
        data.Blocker = null;
        data.PendingDispatch = new ContinueWorkerDispatch(Cursor(data));

        await RegisterDispatchReminderAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await SaveAsync(data).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await TryDispatchPendingAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public Task HandleAsync(AttemptSucceeded fact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fact);

        if (fact.Result is null || fact.Evidence is null)
        {
            return Task.CompletedTask;
        }

        var data = Load();

        if (!Matches(data, fact)
            || data.State is ExecutionState.Succeeded or ExecutionState.Failed or ExecutionState.Cancelled
            || IsOutcomeUncertain(data))
        {
            return Task.CompletedTask;
        }

        data.State = ExecutionState.Succeeded;
        data.ActiveAttempt = null;
        data.Blocker = null;
        data.Result = fact.Result;
        data.Failure = null;
        data.Evidence = [.. fact.Evidence];
        data.PendingDispatch = null;

        Stage(data);
        return Task.CompletedTask;
    }

    public async Task HandleAsync(AttemptFailed fact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fact);

        if (fact.Failure is null)
        {
            return;
        }

        var data = Load();

        if (!Matches(data, fact) || IsTerminal(data.State) || IsOutcomeUncertain(data))
        {
            return;
        }

        var mayAutoRetry = fact.Retryable
            && data.State != ExecutionState.Cancelling
            && data.AttemptCount < data.Policy.MaximumAttempts
            && (data.Policy.Deadline is null || data.Policy.Deadline > DateTimeOffset.UtcNow);

        // Started non-idempotent work (Dispatched) must never auto-retry — force OutcomeUncertain.
        if (mayAutoRetry && TryMarkDispatchedOperationsUncertain(data, out var uncertainBlocker))
        {
            data.State = ExecutionState.Waiting;
            data.Blocker = new OutcomeUncertain(uncertainBlocker);
            data.Result = null;
            data.Failure = null;
            data.Evidence = [];
            data.PendingDispatch = null;

            await UnregisterReminderAsync(RetryReminderName).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await SaveAsync(data).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await EmitAsync(new AttemptOutcomeUncertain(
                Id,
                data.Worker,
                fact.Attempt,
                data.Revision,
                uncertainBlocker)).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        data.ActiveAttempt = null;
        data.Blocker = null;
        data.Result = null;
        data.Failure = fact.Failure;
        data.Evidence = [];
        data.PendingDispatch = null;

        if (mayAutoRetry)
        {
            data.State = ExecutionState.Waiting;
            data.Blocker = new RetryScheduled(new BlockerId(Guid.NewGuid()));
            await this.RegisterOrUpdateReminder(RetryReminderName, data.Policy.RetryDelay, ReminderPeriod)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            Stage(data);
            return;
        }

        data.State = ExecutionState.Failed;

        Stage(data);
    }

    public Task HandleAsync(AttemptCancelled fact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fact);

        var data = Load();

        if (!Matches(data, fact) || data.State != ExecutionState.Cancelling)
        {
            return Task.CompletedTask;
        }

        data.State = ExecutionState.Cancelled;
        data.ActiveAttempt = null;
        data.Blocker = null;
        data.PendingDispatch = null;

        Stage(data);
        return Task.CompletedTask;
    }

    public Task HandleAsync(AttemptOutcomeUncertain fact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fact);

        if (fact.Blocker.Value == Guid.Empty)
        {
            return Task.CompletedTask;
        }

        var data = Load();

        if (!Matches(data, fact) || IsTerminal(data.State))
        {
            return Task.CompletedTask;
        }

        data.State = ExecutionState.Waiting;
        data.Blocker = new OutcomeUncertain(fact.Blocker);
        data.PendingDispatch = null;

        Stage(data);
        return Task.CompletedTask;
    }
}
