using DigitalBrain.Abstractions;
using DigitalBrain.Core;

namespace DigitalBrain.Execution;

internal sealed class ExecutionOperationResolver(
    ExecutionRuntime runtime,
    ExecutionDispatcher dispatcher)
{
    internal async Task<ExecutionSnapshot> ResolveAsync(
        CommandId commandId,
        long? expectedRevision,
        ResolveOperation command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command.OperationKey);

        var data = runtime.Load();

        if (data.Receipts.TryGetValue(commandId, out var received))
        {
            return received;
        }

        if (expectedRevision is null)
        {
            throw new NeuronAuthorizationException(
                "ResolveOperation requires ExpectedRevision.");
        }

        if (expectedRevision.Value != data.Revision)
        {
            throw new NeuronAuthorizationException(
                $"Execution '{runtime.Id}' is at revision {data.Revision}, not expected revision "
                + $"{expectedRevision.Value}.");
        }

        if (ExecutionModel.IsTerminal(data.State))
        {
            throw new NeuronAuthorizationException(
                $"Execution '{runtime.Id}' is terminal and cannot reconcile operation "
                + $"'{command.OperationKey}'.");
        }

        var operations = ExecutionOperationLedger.Operations(data);
        var key = ExecutionOperationLedger.NormalizeKey(command.OperationKey);
        if (!operations.TryGetValue(key, out var operation)
            || operation.Phase != OperationPhase.Uncertain)
        {
            throw new NeuronAuthorizationException(
                $"Execution '{runtime.Id}' has no uncertain operation '{key}' to resolve.");
        }

        if (data.Blocker is not OutcomeUncertain)
        {
            throw new NeuronAuthorizationException(
                $"Execution '{runtime.Id}' is not blocked on an uncertain outcome.");
        }

        switch (command.Resolution)
        {
            case OperationResolution.Completed:
                if (command.ResponsePayload is null || command.ResponsePayload.Value.Id == Guid.Empty)
                {
                    throw new NeuronAuthorizationException(
                        "ResolveOperation Completed requires a non-empty response payload reference.");
                }

                operations[key] = operation with
                {
                    Phase = OperationPhase.Completed,
                    ResponsePayload = command.ResponsePayload,
                    RedactedSummary = command.RedactedSummary,
                };
                Resume(data, operations);
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
                Resume(data, operations);
                break;

            default:
                throw new NeuronAuthorizationException(
                    $"Unknown operation resolution '{command.Resolution}'.");
        }

        var snapshot = ExecutionModel.Snapshot(data);
        ExecutionModel.RememberReceipt(data, commandId, snapshot);

        if (data.PendingDispatch is not null)
        {
            await dispatcher.RegisterDispatchReminderAsync()
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        else
        {
            await dispatcher.UnregisterReminderAsync(ExecutionReminders.Dispatch)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }

        if (data.State == ExecutionState.Failed)
        {
            await dispatcher.UnregisterReminderAsync(ExecutionReminders.Retry)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }

        await runtime.SaveAsync(data)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        if (data.State is ExecutionState.Failed or ExecutionState.Waiting)
        {
            await runtime.NotifyOriginOfStateAsync(data)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }

        await dispatcher.TryDispatchPendingAsync()
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        return snapshot;
    }

    private void Resume(
        ExecutionData data,
        Dictionary<string, OperationSnapshot> operations)
    {
        data.Operations = operations;
        data.Revision++;
        data.State = ExecutionState.Running;
        data.Blocker = null;
        if (data.ActiveAttempt is not null)
        {
            data.PendingDispatch = new ContinueWorkerDispatch(
                ExecutionModel.Cursor(runtime.Id, data));
        }
    }
}
