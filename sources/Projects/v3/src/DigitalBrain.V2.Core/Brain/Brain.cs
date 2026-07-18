using DigitalBrain.V2.Core.Runtime;
using DigitalBrain.V2.Core.Synapses;

namespace DigitalBrain.V2.Core.Brain;

public sealed class Brain : Neuron, IDigitalBrain
{
    public const string WellKnownKey = "brain";

    // Inject from outside the substrate. Reuses the same Emit/Ask the neurons use, so an
    // externally-fired synapse is indistinguishable from one a neuron fired.
    public Task Fire(Synapse synapse, CancellationToken ct = default) =>
        synapse.Routing == RoutingMode.Broadcast ? Emit(synapse) : Ask(synapse.Receiver, synapse);
}
