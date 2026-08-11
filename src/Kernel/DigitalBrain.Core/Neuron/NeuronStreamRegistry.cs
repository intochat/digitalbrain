using System.Collections.Concurrent;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Core;

internal sealed class NeuronStreamRegistry(Neuron neuron)
{
    private readonly ConcurrentDictionary<Guid, CorrelationId> _clientCorrelations = new();
    private readonly ConcurrentDictionary<Guid, GrainId?> _enumerationInitiators = new();
    private readonly ConcurrentDictionary<Guid, SynapseDelivery> _capabilityRequests = new();

    internal CorrelationId? AmbientClientCorrelation { get; private set; }

    internal int PendingCapabilityRequests => _capabilityRequests.Count;

    internal IReadOnlyList<Guid> BoundEnumerations => [.. _enumerationInitiators.Keys];

    internal CorrelationId? EnterClientCorrelation(CorrelationId correlation)
    {
        var previous = AmbientClientCorrelation;
        AmbientClientCorrelation = correlation;
        return previous;
    }

    internal void RestoreClientCorrelation(CorrelationId? correlation)
        => AmbientClientCorrelation = correlation;

    internal void RegisterClientCorrelation(Guid enumerationId, CorrelationId correlation)
        => _clientCorrelations[enumerationId] = correlation;

    internal bool TryGetClientCorrelation(Guid enumerationId, out CorrelationId correlation)
        => _clientCorrelations.TryGetValue(enumerationId, out correlation);

    internal void ForgetClientCorrelation(Guid enumerationId)
        => _clientCorrelations.TryRemove(enumerationId, out _);

    internal void BindEnumeration(Guid enumerationId, GrainId? initiator)
        => _enumerationInitiators[enumerationId] = initiator;

    internal void RequireEnumerationInitiator(Guid enumerationId, GrainId? caller)
    {
        if (!_enumerationInitiators.TryGetValue(enumerationId, out var initiator))
        {
            throw new NeuronAuthorizationException(
                $"Enumeration '{enumerationId}' is not bound to an initiator on neuron "
                + $"'{neuron.Id}', so '{nameof(IAsyncEnumerableGrainExtension.MoveNext)}' and "
                + $"'{nameof(IAsyncEnumerableGrainExtension.DisposeAsync)}' are refused.");
        }

        if (!GrainIdEquals(initiator, caller))
        {
            throw new NeuronAuthorizationException(
                $"Enumeration '{enumerationId}' on neuron '{neuron.Id}' can be continued or "
                + "disposed only by its initiator.");
        }
    }

    internal void ReleaseEnumeration(Guid enumerationId)
        => _enumerationInitiators.TryRemove(enumerationId, out _);

    internal bool TryRegisterCapabilityRequest(Guid enumerationId, SynapseDelivery request)
        => _capabilityRequests.TryAdd(enumerationId, request);

    internal bool TryClaimCapabilityRequest(Guid enumerationId, out SynapseDelivery request)
        => _capabilityRequests.TryRemove(enumerationId, out request!);

    private static bool GrainIdEquals(GrainId? left, GrainId? right)
    {
        if (left is null && right is null)
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return left.Value.Equals(right.Value);
    }
}
