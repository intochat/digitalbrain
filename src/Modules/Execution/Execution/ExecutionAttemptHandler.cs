using DigitalBrain.Abstractions;
using DigitalBrain.Core;

namespace DigitalBrain.Execution;

internal sealed class ExecutionAttemptHandler(
    ExecutionRuntime runtime,
    ExecutionDispatcher dispatcher)
{
    internal async Task AcceptedAsync(AttemptAccepted fact)
    {
        ArgumentNullException.ThrowIfNull(fact);

        var data = runtime.Load();

        if (!ExecutionModel.Matches(runtime.Id, data, fact)
            || data.State != ExecutionState.Pending)
        {
            return;
        }

        data.State = ExecutionState.Running;
        ExecutionModel.AcknowledgePendingDispatch(data, fact);
        runtime.DelayDeactivation(TimeSpan.FromHours(2));
        await runtime.RegisterReminderAsync(
                ExecutionReminders.Dispatch,
                ExecutionLiveness.WorkerLeaseTimeout,
                ExecutionReminders.Period)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        runtime.Stage(data);
    }

    internal async Task RenewLeaseAsync(AttemptCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);

        if (cursor.Execution != runtime.Id)
        {
            throw new NeuronAuthorizationException(
                $"Worker lease for '{cursor.Execution}' cannot renew Execution '{runtime.Id}'.");
        }

        if (!GrainCallerContext.TryGetNeuronId(out var caller) || caller != cursor.Worker)
        {
            throw new NeuronAuthorizationException(
                $"Only attributed worker '{cursor.Worker}' may renew its Execution lease.");
        }

        var data = runtime.LoadIfStarted();
        if (data is null
            || data.State != ExecutionState.Running
            || data.Worker != caller
            || data.ActiveAttempt != cursor.Attempt
            || data.Revision != cursor.Revision)
        {
            return;
        }

        await runtime.RegisterReminderAsync(
                ExecutionReminders.Dispatch,
                ExecutionLiveness.WorkerLeaseTimeout,
                ExecutionReminders.Period)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    internal async Task WaitingAsync(AttemptWaiting fact)
    {
        ArgumentNullException.ThrowIfNull(fact);

        if (fact.Blocker is null)
        {
            return;
        }

        var data = runtime.Load();

        if (!ExecutionModel.Matches(runtime.Id, data, fact)
            || data.State is not (ExecutionState.Pending or ExecutionState.Running or ExecutionState.Waiting)
            || ExecutionModel.IsOutcomeUncertain(data))
        {
            return;
        }

        data.State = ExecutionState.Waiting;
        data.Blocker = fact.Blocker;
        ExecutionModel.AcknowledgePendingDispatch(data, fact);

        runtime.Stage(data);
        await runtime.NotifyOriginOfStateAsync(data)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    internal Task ProgressedAsync(AttemptProgressed fact)
        => AdvanceAsync(fact);

    internal async Task SucceededAsync(AttemptSucceeded fact)
    {
        ArgumentNullException.ThrowIfNull(fact);

        if (fact.Result is null || fact.Evidence is null)
        {
            return;
        }

        var data = runtime.Load();

        if (!ExecutionModel.Matches(runtime.Id, data, fact)
            || data.State is ExecutionState.Succeeded or ExecutionState.Failed or ExecutionState.Cancelled
            || ExecutionModel.IsOutcomeUncertain(data))
        {
            return;
        }

        data.State = ExecutionState.Succeeded;
        data.ActiveAttempt = null;
        data.Blocker = null;
        data.Result = fact.Result;
        data.Failure = null;
        data.Evidence = [.. fact.Evidence];
        data.PendingDispatch = null;
        runtime.DelayDeactivation(TimeSpan.FromMinutes(1));

        await runtime.SaveAsync(data)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await runtime.NotifyOriginOfStateAsync(data)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    internal async Task FailedAsync(AttemptFailed fact)
    {
        ArgumentNullException.ThrowIfNull(fact);

        if (fact.Failure is null)
        {
            return;
        }

        var data = runtime.Load();

        if (!ExecutionModel.Matches(runtime.Id, data, fact)
            || ExecutionModel.IsTerminal(data.State)
            || ExecutionModel.IsOutcomeUncertain(data))
        {
            return;
        }

        var mayAutoRetry = fact.Retryable
            && data.State != ExecutionState.Cancelling
            && data.AttemptCount < data.Policy.MaximumAttempts
            && (data.Policy.Deadline is null || data.Policy.Deadline > DateTimeOffset.UtcNow);

        if (mayAutoRetry
            && ExecutionOperationLedger.TryMarkDispatchedUncertain(
                runtime.Id,
                data,
                out var uncertainBlocker))
        {
            data.State = ExecutionState.Waiting;
            data.Blocker = new OutcomeUncertain(uncertainBlocker);
            data.Result = null;
            data.Failure = null;
            data.Evidence = [];
            data.PendingDispatch = null;

            await dispatcher.UnregisterReminderAsync(ExecutionReminders.Retry)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await runtime.SaveAsync(data)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await runtime.EmitAsync(new AttemptOutcomeUncertain(
                    runtime.Id,
                    data.Worker,
                    fact.Attempt,
                    data.Revision,
                    uncertainBlocker))
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await runtime.NotifyOriginOfStateAsync(data)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
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
            await runtime.RegisterReminderAsync(
                    ExecutionReminders.Retry,
                    data.Policy.RetryDelay,
                    ExecutionReminders.Period)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            runtime.Stage(data);
            await runtime.NotifyOriginOfStateAsync(data)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        data.State = ExecutionState.Failed;
        runtime.DelayDeactivation(TimeSpan.FromMinutes(1));

        await runtime.SaveAsync(data)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await runtime.NotifyOriginOfStateAsync(data)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    internal async Task CancelledAsync(AttemptCancelled fact)
    {
        ArgumentNullException.ThrowIfNull(fact);

        var data = runtime.Load();

        if (!ExecutionModel.Matches(runtime.Id, data, fact)
            || data.State != ExecutionState.Cancelling)
        {
            return;
        }

        data.State = ExecutionState.Cancelled;
        data.ActiveAttempt = null;
        data.Blocker = null;
        data.PendingDispatch = null;
        runtime.DelayDeactivation(TimeSpan.FromMinutes(1));

        await runtime.SaveAsync(data)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await runtime.NotifyOriginOfStateAsync(data)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    internal async Task OutcomeUncertainAsync(AttemptOutcomeUncertain fact)
    {
        ArgumentNullException.ThrowIfNull(fact);

        if (fact.Blocker.Value == Guid.Empty)
        {
            return;
        }

        var data = runtime.Load();

        if (!ExecutionModel.Matches(runtime.Id, data, fact)
            || ExecutionModel.IsTerminal(data.State))
        {
            return;
        }

        data.State = ExecutionState.Waiting;
        data.Blocker = new OutcomeUncertain(fact.Blocker);
        data.PendingDispatch = null;

        runtime.Stage(data);
        await runtime.NotifyOriginOfStateAsync(data)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private async Task AdvanceAsync(AttemptFact fact)
    {
        ArgumentNullException.ThrowIfNull(fact);

        var data = runtime.Load();

        if (!ExecutionModel.Matches(runtime.Id, data, fact)
            || data.State is not (ExecutionState.Running or ExecutionState.Waiting)
            || ExecutionModel.IsOutcomeUncertain(data))
        {
            return;
        }

        data.Revision++;
        data.State = ExecutionState.Running;
        data.Blocker = null;
        data.PendingDispatch = new ContinueWorkerDispatch(
            ExecutionModel.Cursor(runtime.Id, data));

        await dispatcher.RegisterDispatchReminderAsync()
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await runtime.SaveAsync(data)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await dispatcher.TryDispatchPendingAsync()
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }
}
