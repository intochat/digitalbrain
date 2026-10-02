namespace DigitalBrain;

// The space neurons live in — resolution, not switching: synapses form at neurons, the brain
// only finds them. A brain is itself a neuron; every scope is per brain, and the kernel stamps
// which brain a caller acts within.
public interface IDigitalBrain : IAsyncDisposable
{
    T Get<T>(NeuronId neuron) where T : class, INeuron;
}
