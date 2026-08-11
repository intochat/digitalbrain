using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

public sealed partial class ExecutionNeuron
{
    private AttemptCursor Cursor(ExecutionData data) => new(
        Id,
        data.Worker,
        data.ActiveAttempt
            ?? throw new InvalidOperationException($"Execution '{Id}' has no active Attempt."),
        data.Revision);

    private AttemptRequest Request(ExecutionData data) => new(
        Id,
        data.Worker,
        data.ActiveAttempt
            ?? throw new InvalidOperationException($"Execution '{Id}' has no active Attempt."),
        data.Revision,
        data.Goal);

    private bool Matches(ExecutionData data, AttemptFact fact)
    {
        if (fact.Execution != Id
            || fact.Worker != data.Worker
            || fact.Attempt != data.ActiveAttempt)
        {
            return false;
        }

        return fact.Revision == data.Revision;
    }

    private static void AcknowledgePendingDispatch(ExecutionData data, AttemptFact fact)
    {
        var matches = data.PendingDispatch switch
        {
            AcceptWorkerDispatch { Request: var request } =>
                request.Execution == fact.Execution
                && request.Worker == fact.Worker
                && request.Attempt == fact.Attempt
                && request.Revision == fact.Revision,
            ContinueWorkerDispatch { Cursor: var cursor } =>
                cursor.Execution == fact.Execution
                && cursor.Worker == fact.Worker
                && cursor.Attempt == fact.Attempt
                && cursor.Revision == fact.Revision,
            _ => false
        };

        if (matches)
        {
            data.PendingDispatch = null;
        }
    }

    private static ExecutionSnapshot Snapshot(ExecutionData data) => new(
        data.Goal,
        data.Worker,
        data.Policy,
        data.State,
        data.Revision,
        data.ActiveAttempt,
        data.Blocker,
        data.Result,
        data.Failure,
        [.. data.Evidence],
        data.RetryOf,
        data.AttemptCount);

    private static void Validate(StartExecution command)
    {
        ArgumentNullException.ThrowIfNull(command.Goal);
        ArgumentNullException.ThrowIfNull(command.Policy);

        if (command.Policy.MaximumAttempts <= 0)
        {
            throw new NeuronAuthorizationException("An execution policy must allow at least one attempt.");
        }

        if (command.Policy.RetryDelay < TimeSpan.Zero)
        {
            throw new NeuronAuthorizationException("An execution retry delay cannot be negative.");
        }

        if (command.Worker == default)
        {
            throw new NeuronAuthorizationException("An execution worker is required.");
        }
    }

    private static void Validate(CommandId commandId)
    {
        if (commandId.Value == Guid.Empty)
        {
            throw new NeuronAuthorizationException("A command id is required.");
        }
    }

    private async Task ValidatePredecessorAsync(NeuronId? predecessor)
    {
        if (predecessor is null)
        {
            return;
        }

        if (predecessor == Id || predecessor.Value.Owner != Id.Owner)
        {
            throw new NeuronAuthorizationException(
                $"Execution '{predecessor}' cannot be the predecessor of Execution '{Id}'.");
        }

        var snapshot = await GrainFactory
            .GetGrain<IExecution>(predecessor.Value.ToGrainId())
            .Read().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        if (!IsTerminal(snapshot.State))
        {
            throw new NeuronAuthorizationException(
                $"Execution '{predecessor}' is not terminal, so Execution '{Id}' cannot retry it.");
        }
    }

    private static bool IsTerminal(ExecutionState state)
        => state is ExecutionState.Succeeded or ExecutionState.Failed or ExecutionState.Cancelled;

    private static bool IsOutcomeUncertain(ExecutionData data)
        => data.State == ExecutionState.Waiting && data.Blocker is OutcomeUncertain;

    private static void RememberReceipt(ExecutionData data, CommandId commandId, ExecutionSnapshot snapshot)
    {
        data.Receipts[commandId] = snapshot;
        data.ReceiptOrder.Remove(commandId);
        data.ReceiptOrder.Add(commandId);

        while (data.ReceiptOrder.Count > RememberedReceipts)
        {
            var oldest = data.ReceiptOrder[0];
            data.ReceiptOrder.RemoveAt(0);
            data.Receipts.Remove(oldest);
        }
    }

    private static void RememberOperation(
        ExecutionData data,
        string key,
        Dictionary<string, OperationSnapshot> operations)
    {
        data.Operations = operations;
        data.OperationOrder.Remove(key);
        data.OperationOrder.Add(key);

        while (data.OperationOrder.Count > RememberedOperations)
        {
            var oldestKey = data.OperationOrder[0];
            if (operations.TryGetValue(oldestKey, out var oldest)
                && oldest.Phase is not (OperationPhase.Completed or OperationPhase.Failed))
            {
                // Never drop live ledger rows; stop pruning.
                break;
            }

            data.OperationOrder.RemoveAt(0);
            operations.Remove(oldestKey);
        }

        data.Operations = operations;
    }
}
