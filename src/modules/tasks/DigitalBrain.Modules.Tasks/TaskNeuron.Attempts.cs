
namespace DigitalBrain.Tasks;

internal sealed partial class TaskNeuron
{
    public Task HandleAsync(AttemptAccepted fact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fact);

        var data = Load();

        if (!Matches(data, fact) || data.State != TaskState.Pending)
        {
            return Task.CompletedTask;
        }

        data.State = TaskState.Running;
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
            || data.State is not (TaskState.Pending or TaskState.Running or TaskState.Waiting))
        {
            return Task.CompletedTask;
        }

        data.State = TaskState.Waiting;
        data.Blocker = fact.Blocker;
        AcknowledgePendingDispatch(data, fact);

        Stage(data);
        return Task.CompletedTask;
    }

    public async Task HandleAsync(AttemptProgressed fact, CancellationToken cancellationToken)
        => await AdvanceAsync(fact);

    private async Task AdvanceAsync(AttemptFact fact)
    {
        ArgumentNullException.ThrowIfNull(fact);

        var data = Load();

        if (!Matches(data, fact)
            || data.State is not (TaskState.Running or TaskState.Waiting))
        {
            return;
        }

        data.Revision++;
        data.State = TaskState.Running;
        data.Blocker = null;
        data.PendingDispatch = new ContinueWorkerDispatch(Cursor(data));

        await RegisterDispatchReminderAsync();
        await SaveAsync(data);
        await TryDispatchPendingAsync();
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
            || data.State is TaskState.Succeeded or TaskState.Failed or TaskState.Cancelled)
        {
            return Task.CompletedTask;
        }

        data.State = TaskState.Succeeded;
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

        if (!Matches(data, fact) || IsTerminal(data.State))
        {
            return;
        }

        data.ActiveAttempt = null;
        data.Blocker = null;
        data.Result = null;
        data.Failure = fact.Failure;
        data.Evidence = [];
        data.PendingDispatch = null;

        if (fact.Retryable
            && data.State != TaskState.Cancelling
            && data.AttemptCount < data.Policy.MaximumAttempts
            && (data.Policy.Deadline is null || data.Policy.Deadline > DateTimeOffset.UtcNow))
        {
            data.State = TaskState.Waiting;
            data.Blocker = new RetryScheduled(new BlockerId(Guid.NewGuid()));
            await this.RegisterOrUpdateReminder(
                RetryReminderName,
                data.Policy.RetryDelay,
                ReminderPeriod);
            Stage(data);
            return;
        }

        data.State = TaskState.Failed;

        Stage(data);
    }

    public Task HandleAsync(AttemptCancelled fact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fact);

        var data = Load();

        if (!Matches(data, fact) || data.State != TaskState.Cancelling)
        {
            return Task.CompletedTask;
        }

        data.State = TaskState.Cancelled;
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

        data.State = TaskState.Waiting;
        data.Blocker = new OutcomeUncertain(fact.Blocker);
        data.PendingDispatch = null;

        Stage(data);
        return Task.CompletedTask;
    }
}
