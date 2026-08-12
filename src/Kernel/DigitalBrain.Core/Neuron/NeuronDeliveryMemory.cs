using System.Collections.Concurrent;
using System.Reflection;
using DigitalBrain.Abstractions;
using Orleans.Journaling;

namespace DigitalBrain.Core;

internal sealed class NeuronDeliveryMemory(
    Neuron neuron,
    IDurableList<Guid> handled)
{
    private static readonly ConcurrentDictionary<Type, bool> SettledFailureTypes = new();

    private readonly List<Guid> _evictedDuringTurn = [];
    private readonly HashSet<SynapseId> _remembered = [];

    internal bool Contains(SynapseDelivery delivery)
        => _remembered.Contains(delivery.SynapseId);

    internal static bool Settles(Exception failure)
    {
        for (var cursor = failure; cursor is not null; cursor = cursor.InnerException)
        {
            if (cursor is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    if (Settles(inner))
                    {
                        return true;
                    }
                }

                continue;
            }

            if (SettledFailureTypes.GetOrAdd(
                cursor.GetType(),
                static type =>
                {
                    for (var walk = type; walk is not null; walk = walk.BaseType)
                    {
                        if (walk.GetCustomAttribute<SettledDeliveryFailureAttribute>() is not null)
                        {
                            return true;
                        }
                    }

                    return false;
                }))
            {
                return true;
            }
        }

        return false;
    }

    internal void Activate()
    {
        _remembered.Clear();

        foreach (var delivered in handled)
        {
            _remembered.Add(new SynapseId(delivered));
        }
    }

    internal void BeginTurn() => _evictedDuringTurn.Clear();

    internal void EndTurn() => _evictedDuringTurn.Clear();

    internal void Remember(SynapseId delivered)
    {
        handled.Add(delivered.Value);
        _remembered.Add(delivered);

        while (handled.Count > neuron.RememberedDeliveryBound)
        {
            _remembered.Remove(new SynapseId(handled[0]));
            _evictedDuringTurn.Add(handled[0]);
            handled.RemoveAt(0);
        }
    }

    internal void Forget(SynapseDelivery delivery)
    {
        for (var index = handled.Count - 1; index >= 0; index--)
        {
            if (handled[index] == delivery.SynapseId.Value)
            {
                handled.RemoveAt(index);
                break;
            }
        }

        for (var index = _evictedDuringTurn.Count - 1; index >= 0; index--)
        {
            handled.Insert(0, _evictedDuringTurn[index]);
        }
    }
}
