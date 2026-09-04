using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Core;

// Broadcast audience is this neuron's synapses of that signal type. IHandle is the
// capability to receive; it does not subscribe every instance of a type.
public sealed class SignalRouter
{
    internal IReadOnlyList<NeuronId> Resolve(Signal signal, NeuronId self, SynapseSet learned)
    {
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(learned);

        var receivers = new List<NeuronId>();
        // Seeded with self: a broadcaster must never receive its own broadcast.
        var seen = new HashSet<NeuronId> { self };
        foreach (var synapse in learned.For(signal.GetType().Name))
        {
            if (seen.Add(synapse.Target))
            {
                receivers.Add(synapse.Target);
            }
        }

        return receivers;
    }
}
