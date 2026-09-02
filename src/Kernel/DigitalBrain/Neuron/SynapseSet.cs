using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Synapses;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace DigitalBrain.Core;

// A neuron's outgoing edges, held in its own durable state (spec D7). Keyed by target+signal
// type so the same pair of neurons can carry two differently-typed synapses.
internal sealed class SynapseSet
{
    private const string StateName = "synapses";

    private readonly IDurableDictionary<string, Synapse> _synapses;
    private readonly SynapseOptions _options;
    private readonly TimeProvider _time;
    private readonly NeuronId _owner;

    internal SynapseSet(IServiceProvider services, NeuronId owner, TimeProvider time)
    {
        _synapses = services.GetRequiredKeyedService<IDurableDictionary<string, Synapse>>(StateName);
        _options = services.GetService<SynapseOptions>() ?? new SynapseOptions();
        _owner = owner;
        _time = time;
    }

    internal static string KeyFor(NeuronId target, string signalType)
        => $"{target} {signalType}";

    // Ordered strongest-first, with decay applied. Callers see the CURRENT strength, not the
    // stored one — which is what makes read-time decay work without any background job.
    internal IReadOnlyList<Synapse> All()
    {
        var now = _time.GetUtcNow();

        return [.. _synapses.Values.OrderByDescending(synapse => synapse.WeightAt(now, _options.HalfLife))];
    }

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

        var potentiated = current.Kind == SynapseKind.Innate
            ? current with { }
            : current.Potentiate(now, _options.PotentiationRate);

        _synapses[key] = potentiated;
        return potentiated;
    }

    // Storage reclamation only. Driven by ONE reminder per neuron, never one per synapse.
    internal int Prune()
    {
        var now = _time.GetUtcNow();

        var dead = _synapses
            .Where(entry => entry.Value.IsPrunedAt(now, _options.HalfLife, _options.PruneFloor))
            .Select(entry => entry.Key)
            .ToArray();

        foreach (var key in dead)
        {
            _synapses.Remove(key);
        }

        return dead.Length;
    }
}
