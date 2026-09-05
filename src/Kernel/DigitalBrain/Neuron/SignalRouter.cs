using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Core;

// Broadcast audience is this neuron's synapses of that signal type. IHandle is the
// capability to receive; it does not subscribe every instance of a type.
public sealed class SignalRouter
{
    internal IReadOnlyList<NeuronId> BroadcastRecipientsFor(Signal signal, NeuronId source, NeuronSynapses synapses)
    {
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(synapses);

        var receivers = new List<NeuronId>();
        // Seeded with self: a broadcaster must never receive its own broadcast.
        var seen = new HashSet<NeuronId> { source };
        foreach (var synapse in synapses.ForSignal(signal.GetType().Name))
        {
            if (seen.Add(synapse.Target))
            {
                receivers.Add(synapse.Target);
            }
        }

        return receivers;
    }
}
