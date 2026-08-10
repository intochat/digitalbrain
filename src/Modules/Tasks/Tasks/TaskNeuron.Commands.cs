using DigitalBrain.Abstractions;

namespace DigitalBrain.Tasks;

public sealed partial class TaskNeuron
{
    public async Task<TaskSnapshot> Start(StartTask command)
    {
        ArgumentNullException.ThrowIfNull(command);
        Validate(command.CommandId);

        if (_state.Value is { Length: > 0 })
        {
            var existing = Load();

            if (existing.Receipts.TryGetValue(command.CommandId, out var received))
            {
                if (!Equals(existing.Goal, command.Goal)
                    || existing.Worker != command.Worker
                    || existing.Policy != command.Policy
                    || existing.RetryOf != command.RetryOf)
                {
                    throw new InvalidOperationException(
                        $"Task '{Id}' received CommandId '{command.CommandId}' with a different Start payload.");
                }

                return received;
            }

            throw new InvalidOperationException($"Task '{Id}' has already been started.");
        }

        Validate(command);

        if (command.Worker.Owner != Id.Owner)
        {
            throw new InvalidOperationException(
                $"Worker '{command.Worker}' does not belong to Task '{Id}'s owner.");
        }

        await ValidatePredecessorAsync(command.RetryOf).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        var attempt = new AttemptId(Guid.NewGuid());
        var data = new TaskData(
            command.Goal,
            command.Worker,
            command.Policy,
            TaskState.Pending,
            revision: 0,
            activeAttempt: attempt,
            blocker: null,
            result: null,
            failure: null,
            evidence: [],
            command.RetryOf,
            attemptCount: 1,
            receipts: new Dictionary<CommandId, TaskSnapshot>(),
            pendingDispatch: null,
            operations: new Dictionary<string, TaskOperationSnapshot>());
        data.PendingDispatch = new AcceptWorkerDispatch(Request(data));
        var snapshot = Snapshot(data);
        data.Receipts.Add(command.CommandId, snapshot);

        await RegisterDispatchReminderAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await SaveAsync(data).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await TryDispatchPendingAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        return snapshot;
    }

    public async Task<TaskSnapshot> Cancel(CancelTask command)
    {
        ArgumentNullException.ThrowIfNull(command);
        Validate(command.CommandId);

        var data = Load();

        if (data.Receipts.TryGetValue(command.CommandId, out var received))
        {
            return received;
        }

        if (command.ExpectedRevision != data.Revision)
        {
            throw new InvalidOperationException(
                $"Task '{Id}' is at revision {data.Revision}, not expected revision {command.ExpectedRevision}.");
        }

        if (IsTerminal(data.State))
        {
            var terminal = Snapshot(data);
            data.Receipts.Add(command.CommandId, terminal);
            await SaveAsync(data).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return terminal;
        }

        if (data.ActiveAttempt is null)
        {
            data.State = TaskState.Cancelled;
            data.Blocker = null;
            data.PendingDispatch = null;

            var cancelled = Snapshot(data);
            data.Receipts.Add(command.CommandId, cancelled);
            await SaveAsync(data).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await UnregisterReminderAsync(RetryReminderName).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await UnregisterReminderAsync(DispatchReminderName).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return cancelled;
        }

        if (TryMarkDispatchedOperationsUncertain(data, out var uncertainBlocker))
        {
            data.State = TaskState.Waiting;
            data.Blocker = new OutcomeUncertain(uncertainBlocker);
            data.PendingDispatch = new CancelWorkerDispatch(Cursor(data));

            var uncertainSnapshot = Snapshot(data);
            data.Receipts.Add(command.CommandId, uncertainSnapshot);
            await RegisterDispatchReminderAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await SaveAsync(data).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await EmitAsync(new AttemptOutcomeUncertain(
                Id,
                data.Worker,
                data.ActiveAttempt.Value,
                data.Revision,
                uncertainBlocker)).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await UnregisterReminderAsync(RetryReminderName).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await TryDispatchPendingAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return uncertainSnapshot;
        }

        data.State = TaskState.Cancelling;
        data.Blocker = null;
        data.PendingDispatch = new CancelWorkerDispatch(Cursor(data));

        var snapshot = Snapshot(data);
        data.Receipts.Add(command.CommandId, snapshot);

        await RegisterDispatchReminderAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await SaveAsync(data).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await TryDispatchPendingAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        return snapshot;
    }

    private bool TryMarkDispatchedOperationsUncertain(TaskData data, out BlockerId blockerId)
    {
        blockerId = default;
        var operations = data.Operations;
        if (operations is null || operations.Count == 0 || data.ActiveAttempt is null)
        {
            return false;
        }

        var attempt = data.ActiveAttempt.Value;
        BlockerId? first = null;
        var changed = false;
        foreach (var (key, operation) in operations)
        {
            if (operation.Attempt != attempt || operation.Phase != TaskOperationPhase.Dispatched)
            {
                continue;
            }

            var id = OperationBlockerId(Id, attempt, operation.Sequence);
            first ??= id;
            operations[key] = operation with
            {
                Phase = TaskOperationPhase.Uncertain,
                ResponsePayload = null,
            };
            changed = true;
        }

        if (!changed || first is null)
        {
            return false;
        }

        data.Operations = operations;
        blockerId = first.Value;
        return true;
    }
}
