using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Abstractions;
using DigitalBrain.Core;

namespace DigitalBrain.Execution;

internal static class ExecutionOperationLedger
{
    internal const int Capacity = 64;

    internal static Dictionary<string, OperationSnapshot> Operations(ExecutionData data)
        => data.Operations ?? new Dictionary<string, OperationSnapshot>(StringComparer.Ordinal);

    internal static string NormalizeKey(string operationKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKey);
        return operationKey.Trim();
    }

    internal static void RequireActiveAttempt(ExecutionData data, AttemptId attempt)
    {
        if (ExecutionModel.IsTerminal(data.State))
        {
            throw new NeuronAuthorizationException("A terminal execution cannot accept operation commands.");
        }

        if (data.ActiveAttempt is null)
        {
            throw new NeuronAuthorizationException(
                "An execution with no active attempt cannot accept operation commands.");
        }

        if (data.ActiveAttempt != attempt)
        {
            throw new NeuronAuthorizationException(
                $"Operation attempt '{attempt.Value:N}' does not match active attempt "
                + $"'{data.ActiveAttempt.Value.Value:N}'.");
        }
    }

    internal static void ValidateEdge(OperationEdge edge)
    {
        ArgumentNullException.ThrowIfNull(edge);

        if (edge.Target == default
            || string.IsNullOrWhiteSpace(edge.Target.Type)
            || string.IsNullOrWhiteSpace(edge.Target.Name))
        {
            throw new NeuronAuthorizationException(
                "Operation edge requires a non-default target neuron id.");
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

    internal static void ValidateReference(ProtectedPayloadReference reference, string paramName)
    {
        if (reference.Id == Guid.Empty)
        {
            throw new NeuronAuthorizationException(
                $"Protected payload reference cannot be empty ({paramName}).");
        }
    }

    internal static void ValidateTransition(
        OperationPhase current,
        OperationPhase target,
        ProtectedPayloadReference? responsePayload)
    {
        switch (current, target)
        {
            case (OperationPhase.Prepared, OperationPhase.Dispatched):
                if (responsePayload is not null)
                {
                    throw new NeuronAuthorizationException(
                        "Prepared→Dispatched cannot carry a response reference.");
                }

                break;

            case (OperationPhase.Dispatched, OperationPhase.Completed):
                if (responsePayload is null || responsePayload.Value.Id == Guid.Empty)
                {
                    throw new NeuronAuthorizationException(
                        "Dispatched→Completed requires a non-empty response reference.");
                }

                break;

            case (OperationPhase.Dispatched, OperationPhase.Uncertain):
                if (responsePayload is not null)
                {
                    throw new NeuronAuthorizationException(
                        "Dispatched→Uncertain cannot carry a response reference.");
                }

                break;

            case (OperationPhase.Dispatched, OperationPhase.Failed):
                break;

            default:
                throw new NeuronAuthorizationException(
                    $"Transition from '{current}' to '{target}' is not allowed.");
        }
    }

    internal static bool EdgesEqual(OperationEdge left, OperationEdge right)
        => left.Target == right.Target
            && string.Equals(left.RequestSynapseId, right.RequestSynapseId, StringComparison.Ordinal)
            && left.RequestSchemaVersion == right.RequestSchemaVersion
            && string.Equals(left.ResponseSynapseId, right.ResponseSynapseId, StringComparison.Ordinal)
            && left.ResponseSchemaVersion == right.ResponseSchemaVersion;

    internal static BlockerId BlockerId(NeuronId execution, string operationKey)
    {
        var material = $"{execution}:{operationKey}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        var guidBytes = hash.AsSpan(0, 16).ToArray();
        if (guidBytes.All(static value => value == 0))
        {
            guidBytes[^1] = 1;
        }

        return new BlockerId(new Guid(guidBytes));
    }

    internal static void Remember(
        ExecutionData data,
        string key,
        Dictionary<string, OperationSnapshot> operations)
    {
        data.Operations = operations;
        data.OperationOrder.Remove(key);
        data.OperationOrder.Add(key);

        while (operations.Count > Capacity)
        {
            var removableIndex = data.OperationOrder.FindIndex(candidate =>
                !operations.TryGetValue(candidate, out var operation)
                || operation.Phase is OperationPhase.Completed or OperationPhase.Failed);
            if (removableIndex < 0)
            {
                throw new NeuronAuthorizationException(
                    $"Execution operation capacity ({Capacity}) is full of unresolved effects; "
                    + "complete or reconcile an existing operation before preparing another.");
            }

            var removableKey = data.OperationOrder[removableIndex];
            data.OperationOrder.RemoveAt(removableIndex);
            operations.Remove(removableKey);
        }

        data.Operations = operations;
    }

    internal static bool TryMarkDispatchedUncertain(
        NeuronId execution,
        ExecutionData data,
        out BlockerId blockerId)
    {
        blockerId = default;
        var operations = data.Operations;
        if (operations is null || operations.Count == 0)
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

            var id = BlockerId(execution, key);
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
