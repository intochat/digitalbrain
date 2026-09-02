using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Core;

// Assembles the receiver set from tier 1 (innate) and tier 2 (learned). Tier 3 — similarity
// search — belongs to the later discovery-and-learning slice and is deliberately absent: a miss
// here returns an empty set rather than guessing, which keeps this substrate deterministic.
public sealed class SignalRouter(SignalHandlerIndex index)
{
    private readonly SignalHandlerIndex _index = index;

    internal IReadOnlyList<NeuronId> Resolve(Signal signal, NeuronId self, SynapseSet learned)
    {
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(learned);

        var signalType = signal.GetType();

        // Tier 2 first: a learned edge carries a weight and an ordering that tier 1 cannot.
        var receivers = new List<NeuronId>();
        // Seeded with self: a broadcaster must never receive its own broadcast. Skipping it
        // here (rather than dispatching in place, the way an awaited self-send does) is
        // deliberate — in-place self-dispatch still lets a handler that re-broadcasts the
        // signal it handles loop forever, where exclusion makes that unreachable by
        // construction. Do not "fix" this back to self-delivery.
        var seen = new HashSet<NeuronId> { self };

        foreach (var synapse in learned.For(signalType.Name))
        {
            if (seen.Add(synapse.Target))
            {
                receivers.Add(synapse.Target);
            }
        }

        // Tier 1 fills in every declared handler the graph has not learned an edge to yet.
        foreach (var grainType in _index.ReceiversOf(signalType))
        {
            var id = new NeuronId(grainType, self.Owner, "default");
            if (seen.Add(id))
            {
                receivers.Add(id);
            }
        }

        return receivers;
    }
}
