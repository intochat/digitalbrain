using Orleans.Journaling;

namespace Brain.Kernel;

public abstract class Neuron([NeuronState] NeuronDurableState durableState) : DurableGrain
{
    protected NeuronDurableState DurableState { get; } = durableState;
}
