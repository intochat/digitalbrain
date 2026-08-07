using DigitalBrain.Abstractions;
using DigitalBrain.Core;

namespace DigitalBrain.Behaviors.Runtime;

internal sealed class BehaviorCapabilityEdgeResolution
{
    public BehaviorCapabilityEdgeResolution(
        BehaviorCapabilityEdge edge,
        NeuronId deliveryTarget,
        Type requestType,
        Type responseType)
    {
        ArgumentNullException.ThrowIfNull(edge);
        ArgumentNullException.ThrowIfNull(requestType);
        ArgumentNullException.ThrowIfNull(responseType);
        Edge = edge;
        DeliveryTarget = deliveryTarget;
        RequestType = requestType;
        ResponseType = responseType;
    }

    public BehaviorCapabilityEdge Edge { get; }
    public NeuronId DeliveryTarget { get; }
    public Type RequestType { get; }
    public Type ResponseType { get; }
}

internal static class BehaviorCapabilityEdgeAuthority
{
    public static BehaviorCapabilityEdgeResolution ResolveExact(
        OwnerId owner,
        BehaviorCapabilityEdge edge,
        ActiveCapabilityCatalog catalog,
        ActiveModuleContractTypeMap typeMap)
    {
        ArgumentNullException.ThrowIfNull(edge);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(typeMap);

        if (edge.Target.Owner != owner)
        {
            throw new InvalidOperationException("foreign-target-owner");
        }

        if (!catalog.TryGetNeuron(edge.Target.Type, out var neuron) || neuron is null)
        {
            throw new InvalidOperationException("unknown-target-neuron");
        }

        var acceptedMatch = neuron.Accepted.FirstOrDefault(item =>
            string.Equals(item.ContractId, edge.RequestSynapseId, StringComparison.Ordinal)
            && item.SchemaVersion == edge.RequestSchemaVersion);
        if (acceptedMatch is null)
        {
            var knownId = neuron.Accepted.Any(item =>
                string.Equals(item.ContractId, edge.RequestSynapseId, StringComparison.Ordinal));
            throw new InvalidOperationException(
                knownId ? "incompatible-request-version" : "unknown-request-synapse");
        }

        var emittedMatch = neuron.Emitted.FirstOrDefault(item =>
            string.Equals(item.ContractId, edge.ResponseSynapseId, StringComparison.Ordinal)
            && item.SchemaVersion == edge.ResponseSchemaVersion);
        if (emittedMatch is null)
        {
            var knownId = neuron.Emitted.Any(item =>
                string.Equals(item.ContractId, edge.ResponseSynapseId, StringComparison.Ordinal));
            throw new InvalidOperationException(
                knownId ? "incompatible-response-version" : "unknown-response-synapse");
        }

        if (!typeMap.TryGetNeuronGrainType(edge.Target.Type, out var grainType)
            || string.IsNullOrWhiteSpace(grainType))
        {
            throw new InvalidOperationException("unknown-target-neuron-type");
        }

        if (!typeMap.TryGetSynapseType(edge.RequestSynapseId, edge.RequestSchemaVersion, out var requestType)
            || requestType is null)
        {
            throw new InvalidOperationException("unknown-request-type");
        }

        if (!typeMap.TryGetSynapseType(edge.ResponseSynapseId, edge.ResponseSchemaVersion, out var responseType)
            || responseType is null)
        {
            throw new InvalidOperationException("unknown-response-type");
        }

        if (!IsRequestSynapseOf(requestType, responseType))
        {
            throw new InvalidOperationException("request-response-type-mismatch");
        }

        return new BehaviorCapabilityEdgeResolution(
            edge,
            new NeuronId(grainType, edge.Target.Owner, edge.Target.Name),
            requestType,
            responseType);
    }

    private static bool IsRequestSynapseOf(Type requestType, Type responseType)
    {
        var current = requestType;
        while (current is not null)
        {
            if (current.IsGenericType
                && current.GetGenericTypeDefinition() == typeof(RequestSynapse<>)
                && current.GetGenericArguments()[0] == responseType)
            {
                return true;
            }

            current = current.BaseType;
        }

        return false;
    }
}
