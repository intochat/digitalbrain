using DigitalBrain.Abstractions;
using DigitalBrain.Core;

namespace DigitalBrain.Execution;

internal sealed class ExecutionOperationHandler(ExecutionRuntime runtime)
{
    internal ReadOperationResult Read(ReadOperation request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var data = runtime.Load();
        var key = ExecutionOperationLedger.NormalizeKey(request.OperationKey);
        var operations = ExecutionOperationLedger.Operations(data);
        operations.TryGetValue(key, out var operation);

        return new ReadOperationResult(operation);
    }

    internal async Task<OperationSnapshot> PrepareAsync(
        PrepareOperation request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var data = runtime.Load();
        ExecutionOperationLedger.RequireActiveAttempt(data, request.Attempt);
        ExecutionOperationLedger.ValidateEdge(request.Edge);
        ExecutionOperationLedger.ValidateReference(
            request.RequestPayload,
            nameof(request.RequestPayload));
        var key = ExecutionOperationLedger.NormalizeKey(request.OperationKey);

        var operations = ExecutionOperationLedger.Operations(data);

        if (operations.TryGetValue(key, out var existing))
        {
            if (existing.Phase is OperationPhase.Completed or OperationPhase.Failed)
            {
                return existing;
            }

            if (existing.Phase == OperationPhase.Uncertain)
            {
                throw new NeuronAuthorizationException(
                    $"Execution '{runtime.Id}' operation '{key}' is OutcomeUncertain; reconcile "
                    + "via ResolveOperation before re-preparing.");
            }

            if (!ExecutionOperationLedger.EdgesEqual(existing.Edge, request.Edge))
            {
                throw new NeuronAuthorizationException(
                    $"Execution '{runtime.Id}' operation '{key}' already exists with a different edge.");
            }

            if (existing.Attempt != request.Attempt)
            {
                existing = existing with { Attempt = request.Attempt };
                operations[key] = existing;
                data.Operations = operations;
                await runtime.SaveAsync(data)
                    .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            }

            return existing;
        }

        if (ExecutionModel.IsOutcomeUncertain(data))
        {
            throw new NeuronAuthorizationException(
                $"Execution '{runtime.Id}' is blocked on OutcomeUncertain and cannot prepare "
                + "new operations.");
        }

        var snapshot = new OperationSnapshot(
            key,
            request.Attempt,
            request.Edge,
            request.RequestPayload,
            OperationPhase.Prepared,
            ResponsePayload: null,
            RedactedSummary: null);

        operations[key] = snapshot;
        ExecutionOperationLedger.Remember(data, key, operations);
        cancellationToken.ThrowIfCancellationRequested();
        await runtime.SaveAsync(data)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        return snapshot;
    }

    internal async Task<OperationSnapshot> TransitionAsync(
        TransitionOperation request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var data = runtime.Load();
        ExecutionOperationLedger.RequireActiveAttempt(data, request.Attempt);
        var key = ExecutionOperationLedger.NormalizeKey(request.OperationKey);

        var operations = ExecutionOperationLedger.Operations(data);
        if (!operations.TryGetValue(key, out var existing))
        {
            throw new NeuronAuthorizationException(
                $"Execution '{runtime.Id}' has no operation '{key}'.");
        }

        if (existing.Phase == request.Phase
            && existing.ResponsePayload == request.ResponsePayload
            && existing.RedactedSummary == request.RedactedSummary)
        {
            return existing;
        }

        if (existing.Phase != request.ExpectedPhase)
        {
            throw new NeuronAuthorizationException(
                $"Execution '{runtime.Id}' operation '{key}' is in phase '{existing.Phase}', "
                + $"not expected '{request.ExpectedPhase}'.");
        }

        ExecutionOperationLedger.ValidateTransition(
            existing.Phase,
            request.Phase,
            request.ResponsePayload);

        var snapshot = existing with
        {
            Attempt = request.Attempt,
            Phase = request.Phase,
            ResponsePayload = request.ResponsePayload,
            RedactedSummary = request.RedactedSummary,
        };

        operations[key] = snapshot;
        data.Operations = operations;

        if (request.Phase == OperationPhase.Uncertain)
        {
            var blockerId = ExecutionOperationLedger.BlockerId(runtime.Id, key);
            data.State = ExecutionState.Waiting;
            data.Blocker = new OutcomeUncertain(blockerId);
            data.PendingDispatch = null;

            cancellationToken.ThrowIfCancellationRequested();
            await runtime.SaveAsync(data)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            cancellationToken.ThrowIfCancellationRequested();
            await runtime.EmitAsync(new AttemptOutcomeUncertain(
                    runtime.Id,
                    data.Worker,
                    request.Attempt,
                    data.Revision,
                    blockerId))
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return snapshot;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await runtime.SaveAsync(data)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        return snapshot;
    }
}
