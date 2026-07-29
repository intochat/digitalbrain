using System.Collections.Concurrent;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Kernel;

public abstract partial class Neuron
{
    private readonly ConcurrentDictionary<Guid, GrainId?> _streamedEnumerationInitiators = new();

    internal void BindStreamedEnumeration(Guid enumerationId, GrainId? initiator)
        => _streamedEnumerationInitiators[enumerationId] = initiator;

    internal void RequireStreamedEnumerationInitiator(Guid enumerationId, GrainId? caller)
    {
        if (!_streamedEnumerationInitiators.TryGetValue(enumerationId, out var initiator))
        {
            throw new NeuronAuthorizationException(
                $"Enumeration '{enumerationId}' is not bound to an initiator on neuron '{Id}', so '{nameof(IAsyncEnumerableGrainExtension.MoveNext)}' and '{nameof(IAsyncEnumerableGrainExtension.DisposeAsync)}' are refused.");
        }

        if (!GrainIdEquals(initiator, caller))
        {
            throw new NeuronAuthorizationException(
                $"Enumeration '{enumerationId}' on neuron '{Id}' can be continued or disposed only by its initiator.");
        }
    }

    internal void ReleaseStreamedEnumeration(Guid enumerationId)
        => _streamedEnumerationInitiators.TryRemove(enumerationId, out _);

    internal IReadOnlyList<Guid> BoundStreamedEnumerations
        => [.. _streamedEnumerationInitiators.Keys];

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
