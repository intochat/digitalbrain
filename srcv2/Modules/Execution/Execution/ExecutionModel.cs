using DigitalBrain.Abstractions;
using DigitalBrain.Core;

namespace DigitalBrain.Execution;

internal static class ExecutionModel
{
    internal const int RememberedReceipts = 64;

    internal static AttemptCursor Cursor(NeuronId execution, ExecutionData data) => new(
        execution,
        data.Worker,
        data.ActiveAttempt
            ?? throw new InvalidOperationException($"Execution '{execution}' has no active Attempt."),
        data.Revision);

    internal static AttemptRequest Request(NeuronId execution, ExecutionData data) => new(
        execution,
        data.Worker,
        data.ActiveAttempt
            ?? throw new InvalidOperationException($"Execution '{execution}' has no active Attempt."),
        data.Revision,
        data.Goal);

    internal static bool Matches(NeuronId execution, ExecutionData data, AttemptFact fact)
    {
        if (fact.Execution != execution
            || fact.Worker != data.Worker
            || fact.Attempt != data.ActiveAttempt)
        {
            return false;
        }

        return fact.Revision == data.Revision;
    }

    internal static void AcknowledgePendingDispatch(ExecutionData data, AttemptFact fact)
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
            _ => false,
        };

        if (matches)
        {
            data.PendingDispatch = null;
        }
    }

    internal static ExecutionSnapshot Snapshot(ExecutionData data) => new(
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

    internal static void ValidateStart(StartExecution command)
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

    internal static void ValidateCommandId(CommandId commandId)
    {
        if (commandId.Value == Guid.Empty)
        {
            throw new NeuronAuthorizationException("A command id is required.");
        }
    }

    internal static bool IsTerminal(ExecutionState state)
        => state is ExecutionState.Succeeded or ExecutionState.Failed or ExecutionState.Cancelled;

    internal static bool IsOutcomeUncertain(ExecutionData data)
        => data.State == ExecutionState.Waiting && data.Blocker is OutcomeUncertain;

    internal static void RememberReceipt(
        ExecutionData data,
        CommandId commandId,
        ExecutionSnapshot snapshot)
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
}
