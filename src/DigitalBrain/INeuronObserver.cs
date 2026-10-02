namespace DigitalBrain;

/// <summary>
/// The receiving endpoint of a synapse: the dendrite. Anything a signal can arrive at is an
/// observer — a UI surface, a behavior's script neuron, a kernel pump. When the observer is
/// itself a neuron, its synapse lives in that neuron's state and survives every process; that,
/// and nothing more, is what a durable subscription is.
/// </summary>
public interface INeuronObserver
{
    /// <summary>
    /// Receives one signal crossing a synapse this observer is connected by. Delivery is
    /// at-least-once, so implementations deduplicate through neuron state, never through
    /// process memory.
    /// </summary>
    /// <param name="signal">The fact that crossed, its publisher already stamped by the kernel.</param>
    Task OnSignalAsync(Signal signal);
}
