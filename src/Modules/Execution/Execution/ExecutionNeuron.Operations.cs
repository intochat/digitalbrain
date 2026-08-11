using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

public sealed partial class ExecutionNeuron
{
    public async Task HandleAsync(ReadOperation synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        var data = Load();
        var key = NormalizeOperationKey(synapse.OperationKey);
        var operations = Operations(data);
        operations.TryGetValue(key, out var operation);

        cancellationToken.ThrowIfCancellationRequested();
        await ReplyAsync(new ReadOperationResult(operation), cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public async Task HandleAsync(PrepareOperation synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        var data = Load();
        RequireActiveAttempt(data, synapse.Attempt);
        ValidateEdge(synapse.Edge);
        ValidateReference(synapse.RequestPayload, nameof(synapse.RequestPayload));
        var key = NormalizeOperationKey(synapse.OperationKey);

        var operations = Operations(data);

        if (operations.TryGetValue(key, out var existing))
        {
            // Attempt-stable identity: a completed effect is never repeated by a retry.
            if (existing.Phase is OperationPhase.Completed or OperationPhase.Failed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ReplyAsync(existing, cancellationToken)
                    .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
                return;
            }

            if (existing.Phase == OperationPhase.Uncertain)
            {
                throw new NeuronAuthorizationException(
                    $"Execution '{Id}' operation '{key}' is OutcomeUncertain; reconcile via ResolveOperation before re-preparing.");
            }

            if (!EdgesEqual(existing.Edge, synapse.Edge))
            {
                throw new NeuronAuthorizationException(
                    $"Execution '{Id}' operation '{key}' already exists with a different edge.");
            }

            // Refresh attempt stamp so the active attempt owns the ledger row.
            if (existing.Attempt != synapse.Attempt)
            {
                existing = existing with { Attempt = synapse.Attempt };
                operations[key] = existing;
                data.Operations = operations;
                await SaveAsync(data).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            }

            cancellationToken.ThrowIfCancellationRequested();
            await ReplyAsync(existing, cancellationToken)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        if (IsOutcomeUncertain(data))
        {
            throw new NeuronAuthorizationException(
                $"Execution '{Id}' is blocked on OutcomeUncertain and cannot prepare new operations.");
        }

        var snapshot = new OperationSnapshot(
            key,
            synapse.Attempt,
            synapse.Edge,
            synapse.RequestPayload,
            OperationPhase.Prepared,
            ResponsePayload: null,
            RedactedSummary: null);

        operations[key] = snapshot;
        RememberOperation(data, key, operations);
        cancellationToken.ThrowIfCancellationRequested();
        await SaveAsync(data).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        cancellationToken.ThrowIfCancellationRequested();
        await ReplyAsync(snapshot, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public async Task HandleAsync(TransitionOperation synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        var data = Load();
        RequireActiveAttempt(data, synapse.Attempt);
        var key = NormalizeOperationKey(synapse.OperationKey);

        var operations = Operations(data);
        if (!operations.TryGetValue(key, out var existing))
        {
            throw new NeuronAuthorizationException(
                $"Execution '{Id}' has no operation '{key}'.");
        }

        if (existing.Phase == synapse.Phase
            && existing.ResponsePayload == synapse.ResponsePayload
            && existing.RedactedSummary == synapse.RedactedSummary)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ReplyAsync(existing, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        if (existing.Phase != synapse.ExpectedPhase)
        {
            throw new NeuronAuthorizationException(
                $"Execution '{Id}' operation '{key}' is in phase '{existing.Phase}', not expected '{synapse.ExpectedPhase}'.");
        }

        ValidateTransition(existing.Phase, synapse.Phase, synapse.ResponsePayload);

        var snapshot = existing with
        {
            Attempt = synapse.Attempt,
            Phase = synapse.Phase,
            ResponsePayload = synapse.ResponsePayload,
            RedactedSummary = synapse.RedactedSummary,
        };

        operations[key] = snapshot;
        data.Operations = operations;

        if (synapse.Phase == OperationPhase.Uncertain)
        {
            var blockerId = OperationBlockerId(Id, key);
            data.State = ExecutionState.Waiting;
            data.Blocker = new OutcomeUncertain(blockerId);
            data.PendingDispatch = null;

            cancellationToken.ThrowIfCancellationRequested();
            await SaveAsync(data).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            cancellationToken.ThrowIfCancellationRequested();
            await EmitAsync(new AttemptOutcomeUncertain(
                Id,
                data.Worker,
                synapse.Attempt,
                data.Revision,
                blockerId)).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            cancellationToken.ThrowIfCancellationRequested();
            await ReplyAsync(snapshot, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await SaveAsync(data).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        cancellationToken.ThrowIfCancellationRequested();
        await ReplyAsync(snapshot, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private static Dictionary<string, OperationSnapshot> Operations(ExecutionData data)
        => data.Operations ?? new Dictionary<string, OperationSnapshot>(StringComparer.Ordinal);

    private static string NormalizeOperationKey(string operationKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKey);
        return operationKey.Trim();
    }

    private static void RequireActiveAttempt(ExecutionData data, AttemptId attempt)
    {
        if (IsTerminal(data.State))
        {
            throw new NeuronAuthorizationException("A terminal execution cannot accept operation commands.");
        }

        if (data.ActiveAttempt is null)
        {
            throw new NeuronAuthorizationException("An execution with no active attempt cannot accept operation commands.");
        }

        if (data.ActiveAttempt != attempt)
        {
            throw new NeuronAuthorizationException(
                $"Operation attempt '{attempt.Value:N}' does not match active attempt '{data.ActiveAttempt.Value.Value:N}'.");
        }
    }

    private static void ValidateEdge(OperationEdge edge)
    {
        ArgumentNullException.ThrowIfNull(edge);

        if (edge.Target == default
            || string.IsNullOrWhiteSpace(edge.Target.Type)
            || string.IsNullOrWhiteSpace(edge.Target.Name))
        {
            throw new NeuronAuthorizationException("Operation edge requires a non-default target neuron id.");
        }

        if (string.IsNullOrWhiteSpace(edge.RequestSynapseId))
        {
            throw new NeuronAuthorizationException("Operation edge requires a request synapse id.");
        }

        if (edge.RequestSchemaVersion <= 0)
        {
            throw new NeuronAuthorizationException("Request schema version must be positive.");
        }

        if (string.IsNullOrWhiteSpace(edge.ResponseSynapseId))
        {
            throw new NeuronAuthorizationException("Operation edge requires a response synapse id.");
        }

        if (edge.ResponseSchemaVersion <= 0)
        {
            throw new NeuronAuthorizationException("Response schema version must be positive.");
        }
    }

    private static void ValidateReference(ProtectedPayloadReference reference, string paramName)
    {
        if (reference.Id == Guid.Empty)
        {
            throw new NeuronAuthorizationException($"Protected payload reference cannot be empty ({paramName}).");
        }
    }

    private static void ValidateTransition(
        OperationPhase current,
        OperationPhase target,
        ProtectedPayloadReference? responsePayload)
    {
        switch (current, target)
        {
            case (OperationPhase.Prepared, OperationPhase.Dispatched):
                if (responsePayload is not null)
                {
                    throw new NeuronAuthorizationException("Prepared→Dispatched cannot carry a response reference.");
                }

                break;

            case (OperationPhase.Dispatched, OperationPhase.Completed):
                if (responsePayload is null || responsePayload.Value.Id == Guid.Empty)
                {
                    throw new NeuronAuthorizationException("Dispatched→Completed requires a non-empty response reference.");
                }

                break;

            case (OperationPhase.Dispatched, OperationPhase.Uncertain):
                if (responsePayload is not null)
                {
                    throw new NeuronAuthorizationException("Dispatched→Uncertain cannot carry a response reference.");
                }

                break;

            case (OperationPhase.Dispatched, OperationPhase.Failed):
                break;

            default:
                throw new NeuronAuthorizationException(
                    $"Transition from '{current}' to '{target}' is not allowed.");
        }
    }

    private static bool EdgesEqual(OperationEdge left, OperationEdge right)
        => left.Target == right.Target
            && string.Equals(left.RequestSynapseId, right.RequestSynapseId, StringComparison.Ordinal)
            && left.RequestSchemaVersion == right.RequestSchemaVersion
            && string.Equals(left.ResponseSynapseId, right.ResponseSynapseId, StringComparison.Ordinal)
            && left.ResponseSchemaVersion == right.ResponseSchemaVersion;

    private static BlockerId OperationBlockerId(NeuronId execution, string operationKey)
    {
        var material = $"{execution}:{operationKey}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        var guidBytes = hash.AsSpan(0, 16).ToArray();
        if (guidBytes.All(b => b == 0))
        {
            guidBytes[^1] = 1;
        }

        return new BlockerId(new Guid(guidBytes));
    }
}
