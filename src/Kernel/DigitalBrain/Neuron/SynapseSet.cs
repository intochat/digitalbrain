using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Synapses;
using Orleans.Journaling;

namespace DigitalBrain.Core;

// A neuron's outgoing edges, held in its own durable state (spec D7). Keyed by target+signal
// type so the same pair of neurons can carry two differently-typed synapses.
internal sealed class SynapseSet
{
    private readonly IDurableDictionary<string, Synapse> _synapses;
    private readonly SynapseOptions _options;
    private readonly TimeProvider _time;
    private readonly NeuronId _owner;

    internal SynapseSet(
        IDurableDictionary<string, Synapse> synapses,
        SynapseOptions options,
        NeuronId owner,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(synapses);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);

        _synapses = synapses;
        _options = options;
        _owner = owner;
        _time = time;
    }

    internal static string KeyFor(NeuronId target, string signalType)
        => $"{target} {signalType}";

    // Slice 1 pruning is read/routing exclusion. Physical reclamation belongs to a later
    // storage-maintenance decision. Returned records retain stored Weight and are ordered by
    // effective decayed strength; callers can use WeightAt when they need the current value.
    internal IReadOnlyList<Synapse> All()
    {
        var now = _time.GetUtcNow();

        return
        [
            .. _synapses.Values
                .Where(synapse => !synapse.IsPrunedAt(now, _options.HalfLife, _options.PruneFloor))
                .OrderByDescending(synapse => synapse.WeightAt(now, _options.HalfLife))
        ];
    }

    // Slice 1 pruning is read/routing exclusion. Physical reclamation belongs to a later
    // storage-maintenance decision.
    internal IReadOnlyList<Synapse> For(string signalType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signalType);

        var now = _time.GetUtcNow();

        return
        [
            .. _synapses.Values
                .Where(synapse => string.Equals(synapse.SignalType, signalType, StringComparison.Ordinal))
                .Where(synapse => !synapse.IsPrunedAt(now, _options.HalfLife, _options.PruneFloor))
                .OrderByDescending(synapse => synapse.WeightAt(now, _options.HalfLife))
        ];
    }

    // Hebbian bookkeeping. Call ONLY after the receiver handled the signal — an unhandled
    // delivery must not strengthen the path that produced it.
    internal Synapse Record(NeuronId target, string signalType, SynapseKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signalType);

        var now = _time.GetUtcNow();
        var key = KeyFor(target, signalType);

        var current = _synapses.TryGetValue(key, out var existing)
            ? existing
            : new Synapse(
                _owner,
                target,
                signalType,
                _options.InitialWeightFor(kind),
                now,
                kind,
                isBlocking: false);

        var potentiated = current.Potentiate(now, _options.HalfLife, _options.PotentiationRate);

        _synapses[key] = potentiated;
        return potentiated;
    }
}
