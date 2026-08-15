using DigitalBrain.Abstractions;
using DigitalBrain.Core;

namespace DigitalBrain.Execution;

internal sealed class ExecutionStarter(
    ExecutionRuntime runtime,
    ExecutionDispatcher dispatcher)
{
    internal async Task<ExecutionSnapshot> StartAsync(
        CommandId commandId,
        StartExecution command)
    {
        if (runtime.HasStarted)
        {
            var existing = runtime.Load();

            if (existing.Receipts.TryGetValue(commandId, out var received))
            {
                if (!Equals(existing.Goal, command.Goal)
                    || existing.Worker != command.Worker
                    || existing.Policy != command.Policy
                    || existing.RetryOf != command.RetryOf
                    || existing.Origin != command.Origin)
                {
                    throw new NeuronAuthorizationException(
                        $"Execution '{runtime.Id}' received CommandId '{commandId}' with a "
                        + "different Start payload.");
                }

                return received;
            }

            throw new NeuronAuthorizationException(
                $"Execution '{runtime.Id}' has already been started.");
        }

        ExecutionModel.ValidateStart(command);

        if (command.Worker.Owner != runtime.Id.Owner)
        {
            throw new NeuronAuthorizationException(
                $"Worker '{command.Worker}' does not belong to Execution '{runtime.Id}'s owner.");
        }

        await ValidatePredecessorAsync(command.RetryOf)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

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
            operationOrder: [],
            origin: command.Origin);
        data.PendingDispatch = new AcceptWorkerDispatch(
            ExecutionModel.Request(runtime.Id, data));
        var snapshot = ExecutionModel.Snapshot(data);
        ExecutionModel.RememberReceipt(data, commandId, snapshot);

        await dispatcher.RegisterDispatchReminderAsync()
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await runtime.SaveAsync(data)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await dispatcher.TryDispatchPendingAsync()
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        return snapshot;
    }

    private async Task ValidatePredecessorAsync(NeuronId? predecessor)
    {
        if (predecessor is null)
        {
            return;
        }

        if (predecessor == runtime.Id || predecessor.Value.Owner != runtime.Id.Owner)
        {
            throw new NeuronAuthorizationException(
                $"Execution '{predecessor}' cannot be the predecessor of Execution '{runtime.Id}'.");
        }

        var snapshot = await runtime.GrainFactory
            .GetGrain<IExecution>(predecessor.Value.ToGrainId())
            .Read()
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        if (!ExecutionModel.IsTerminal(snapshot.State))
        {
            throw new NeuronAuthorizationException(
                $"Execution '{predecessor}' is not terminal, so Execution '{runtime.Id}' "
                + "cannot retry it.");
        }
    }
}
