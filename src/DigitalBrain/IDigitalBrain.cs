namespace DigitalBrain;

/// <summary>
/// The space neurons live in. The brain resolves, it does not switch: synapses form at neurons
/// (<see cref="INeuron.Watch"/>), the brain only finds them. A brain is itself a neuron; every
/// scope — registry, signals, apps — is per brain, and the kernel stamps which brain a caller
/// acts within.
/// </summary>
public interface IDigitalBrain : IAsyncDisposable
{
    /// <summary>
    /// Resolves a neuron by identity: the one sanctioned way to turn a <see cref="NeuronId"/>
    /// — say, a signal's <see cref="Signal.Publisher"/> — back into the capability it names.
    /// </summary>
    /// <typeparam name="T">The neuron contract to address it through.</typeparam>
    /// <param name="neuron">The identity of the neuron to resolve.</param>
    T Get<T>(NeuronId neuron) where T : class, INeuron;
}
