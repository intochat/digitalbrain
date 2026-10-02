namespace DigitalBrain;

// The receiving endpoint of a synapse: the dendrite. Anything that can be delivered to is an
// observer — a UI surface, a behavior's script neuron, a kernel pump. When the observer is
// itself a neuron, the synapse lives in that neuron's state and survives every process; that,
// and nothing more, is what a durable subscription is.
public interface INeuronObserver
{
    Task OnSignalAsync(Signal signal);
}
