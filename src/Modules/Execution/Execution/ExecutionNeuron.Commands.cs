using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

public sealed partial class ExecutionNeuron
{
    public async Task<ExecutionSnapshot> Apply(ApplyExecution command)
    {
        ArgumentNullException.ThrowIfNull(command);
        Validate(command.CommandId);
        ArgumentNullException.ThrowIfNull(command.Command);

        return command.Command switch
        {
            StartExecution start => await StartAsync(command.CommandId, start)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext),
            CancelExecution => await CancelAsync(command.CommandId, command.ExpectedRevision)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext),
            ResolveOperation resolve => await ResolveOperationAsync(command.CommandId, command.ExpectedRevision, resolve)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext),
            _ => throw new InvalidOperationException(
                $"Execution '{Id}' does not understand apply command '{command.Command.GetType().Name}'."),
        };
    }

    private async Task<ExecutionSnapshot> StartAsync(CommandId commandId, StartExecution command)
    {
        if (_state.Value is { Length: > 0 })
        {
            var existing = Load();

            if (existing.Receipts.TryGetValue(commandId, out var received))
            {
                if (!Equals(existing.Goal, command.Goal)
                    || existing.Worker != command.Worker
                    || existing.Policy != command.Policy
                    || existing.RetryOf != command.RetryOf)
                {
                    throw new InvalidOperationException(
                        $"Execution '{Id}' received CommandId '{commandId}' with a different Start payload.");
                }

                return received;
            }

            throw new InvalidOperationException($"Execution '{Id}' has already been started.");
        }

        Validate(command);

        if (command.Worker.Owner != Id.Owner)
        {
            throw new InvalidOperationException(
                $"Worker '{command.Worker}' does not belong to Execution '{Id}'s owner.");
        }

        await ValidatePredecessorAsync(command.RetryOf).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        var attempt = new AttemptId(Guid.NewGuid());
        var data = new ExecutionData(
            command.Goal,
            command.Worker,
            command.Policy,
            ExecutionState.Pending,
            revision: 0,
            activeAttempt: attempt,
            blocker: null,
            result: null,
            failure: null,
            evidence: [],
            command.RetryOf,
            attemptCount: 1,
            receipts: new Dictionary<CommandId, ExecutionSnapshot>(),
            receiptOrder: [],
            pendingDispatch: null,
            operations: new Dictionary<string, OperationSnapshot>(StringComparer.Ordinal),
            operationOrder: []);
        data.PendingDispatch = new AcceptWorkerDispatch(Request(data));
        var snapshot = Snapshot(data);
        RememberReceipt(data, commandId, snapshot);

        await RegisterDispatchReminderAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await SaveAsync(data).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await TryDispatchPendingAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        return snapshot;
    }

    private async Task<ExecutionSnapshot> CancelAsync(CommandId commandId, long? expectedRevision)
    {
        var data = Load();

        if (data.Receipts.TryGetValue(commandId, out var received))
        {
            return received;
        }

        if (expectedRevision is null)
        {
            throw new ArgumentException("Cancel requires ExpectedRevision.", nameof(expectedRevision));
        }

        if (expectedRevision.Value != data.Revision)
        {
            throw new InvalidOperationException(
                $"Execution '{Id}' is at revision {data.Revision}, not expected revision {expectedRevision.Value}.");
        }

        if (IsTerminal(data.State))
        {
            var terminal = Snapshot(data);
            RememberReceipt(data, commandId, terminal);
            await SaveAsync(data).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return terminal;
        }

        if (data.ActiveAttempt is null)
        {
            data.State = ExecutionState.Cancelled;
            data.Blocker = null;
            data.PendingDispatch = null;

            var cancelled = Snapshot(data);
            RememberReceipt(data, commandId, cancelled);
            await SaveAsync(data).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await UnregisterReminderAsync(RetryReminderName).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await UnregisterReminderAsync(DispatchReminderName).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return cancelled;
        }

        if (TryMarkDispatchedOperationsUncertain(data, out var uncertainBlocker))
        {
            data.State = ExecutionState.Waiting;
            data.Blocker = new OutcomeUncertain(uncertainBlocker);
            data.PendingDispatch = new CancelWorkerDispatch(Cursor(data));

            var uncertainSnapshot = Snapshot(data);
            RememberReceipt(data, commandId, uncertainSnapshot);
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

        data.State = ExecutionState.Cancelling;
        data.Blocker = null;
        data.PendingDispatch = new CancelWorkerDispatch(Cursor(data));

        var snapshot = Snapshot(data);
        RememberReceipt(data, commandId, snapshot);

        await RegisterDispatchReminderAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await SaveAsync(data).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await TryDispatchPendingAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        return snapshot;
    }

    private async Task<ExecutionSnapshot> ResolveOperationAsync(
        CommandId commandId,
        long? expectedRevision,
        ResolveOperation command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command.OperationKey);

        var data = Load();

        if (data.Receipts.TryGetValue(commandId, out var received))
        {
            return received;
        }

        if (expectedRevision is null)
        {
            throw new ArgumentException("ResolveOperation requires ExpectedRevision.", nameof(expectedRevision));
        }

        if (expectedRevision.Value != data.Revision)
        {
            throw new InvalidOperationException(
                $"Execution '{Id}' is at revision {data.Revision}, not expected revision {expectedRevision.Value}.");
        }

        if (IsTerminal(data.State))
        {
            throw new NeuronAuthorizationException(
                $"Execution '{Id}' is terminal and cannot reconcile operation '{command.OperationKey}'.");
        }

        var operations = Operations(data);
        var key = NormalizeOperationKey(command.OperationKey);
        if (!operations.TryGetValue(key, out var operation) || operation.Phase != OperationPhase.Uncertain)
        {
            throw new NeuronAuthorizationException(
                $"Execution '{Id}' has no uncertain operation '{key}' to resolve.");
        }

        if (data.Blocker is not OutcomeUncertain)
        {
            throw new NeuronAuthorizationException(
                $"Execution '{Id}' is not blocked on an uncertain outcome.");
        }

        switch (command.Resolution)
        {
            case OperationResolution.Completed:
                if (command.ResponsePayload is null || command.ResponsePayload.Value.Id == Guid.Empty)
                {
                    throw new InvalidOperationException(
                        "ResolveOperation Completed requires a non-empty response payload reference.");
                }

                operations[key] = operation with
                {
                    Phase = OperationPhase.Completed,
                    ResponsePayload = command.ResponsePayload,
                    RedactedSummary = command.RedactedSummary,
                };
                data.Operations = operations;
                data.Revision++;
                data.State = ExecutionState.Running;
                data.Blocker = null;
                if (data.ActiveAttempt is not null)
                {
                    data.PendingDispatch = new ContinueWorkerDispatch(Cursor(data));
                }

                break;

            case OperationResolution.Failed:
                operations[key] = operation with
                {
                    Phase = OperationPhase.Failed,
                    ResponsePayload = command.ResponsePayload,
                    RedactedSummary = command.RedactedSummary,
                };
                data.Operations = operations;
                data.State = ExecutionState.Failed;
                data.ActiveAttempt = null;
                data.Blocker = null;
                data.Result = null;
                data.Failure = new OperationFailed(key, command.RedactedSummary);
                data.Evidence = [];
                data.PendingDispatch = null;
                break;

            case OperationResolution.PermitRetry:
                operations[key] = operation with
                {
                    Phase = OperationPhase.Prepared,
                    ResponsePayload = null,
                    RedactedSummary = command.RedactedSummary,
                };
                data.Operations = operations;
                data.Revision++;
                data.State = ExecutionState.Running;
                data.Blocker = null;
                if (data.ActiveAttempt is not null)
                {
                    data.PendingDispatch = new ContinueWorkerDispatch(Cursor(data));
                }

                break;

            default:
                throw new InvalidOperationException(
                    $"Unknown operation resolution '{command.Resolution}'.");
        }

        var snapshot = Snapshot(data);
        RememberReceipt(data, commandId, snapshot);

        if (data.PendingDispatch is not null)
        {
            await RegisterDispatchReminderAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        else
        {
            await UnregisterReminderAsync(DispatchReminderName).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }

        if (data.State == ExecutionState.Failed)
        {
            await UnregisterReminderAsync(RetryReminderName).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }

        await SaveAsync(data).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await TryDispatchPendingAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        return snapshot;
    }

    private bool TryMarkDispatchedOperationsUncertain(ExecutionData data, out BlockerId blockerId)
    {
        blockerId = default;
        var operations = data.Operations;
        if (operations is null || operations.Count == 0 || data.ActiveAttempt is null)
        {
            return false;
        }

        BlockerId? first = null;
        var changed = false;
        foreach (var (key, operation) in operations)
        {
            if (operation.Phase != OperationPhase.Dispatched)
            {
                continue;
            }

            var id = OperationBlockerId(Id, key);
            first ??= id;
            operations[key] = operation with
            {
                Phase = OperationPhase.Uncertain,
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
