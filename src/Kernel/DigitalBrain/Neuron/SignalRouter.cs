using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Core;

// Assembles the receiver set from tier 1 (innate) and tier 2 (learned). Tier 3 — similarity
// search — is slice 4 and deliberately absent: a miss here returns an empty set rather than
// guessing, which keeps every test in this slice deterministic.
public sealed class SignalRouter(SignalHandlerIndex index)
{
    private readonly SignalHandlerIndex _index = index;

    internal IReadOnlyList<NeuronId> Resolve(Signal signal, OwnerId owner, SynapseSet learned)
    {
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(learned);

        var signalType = signal.GetType();

        // Tier 2 first: a learned edge carries a weight and an ordering that tier 1 cannot.
        var receivers = new List<NeuronId>();
        var seen = new HashSet<NeuronId>();

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
            var id = new NeuronId(grainType, owner, "default");
            if (seen.Add(id))
            {
                receivers.Add(id);
            }
        }

        return receivers;
    }
}
