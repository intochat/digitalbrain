namespace DigitalBrain;

/// <summary>
/// A stateful, addressable capability: a chart, a timer, a button, an inbox, a brain. All state
/// lives in neurons, never in a process — processes are caches. A neuron publishes signals only
/// in its own name, and is where synapses form: watching a neuron connects an observer to it.
/// </summary>
public interface INeuron
{
    /// <summary>
    /// Forms a synapse between this neuron and a receiver. Every signal published from now on
    /// — or, when the observer is itself a neuron, from that synapse's watermark — crosses it
    /// until it is severed.
    /// </summary>
    /// <param name="observer">
    /// The receiving endpoint for push delivery, when the receiver is addressable — a UI
    /// surface, a script neuron. Omit it to pull instead: the synapse's own
    /// <see cref="ISynapse.Signals"/> stream is then the only consumer.
    /// </param>
    /// <returns>The formed synapse; disposing it severs the connection.</returns>
    Task<ISynapse> Watch(INeuronObserver? observer = null);

    /// <summary>
    /// Severs this observer's synapse without needing its handle, so anyone cleaning up — an
    /// app uninstalling its script, a kernel reclaiming a dead endpoint — can disconnect it.
    /// Severing is safe at any moment, including while signals are being delivered.
    /// </summary>
    /// <param name="observer">The receiving endpoint whose synapse to sever.</param>
    Task Unwatch(INeuronObserver observer);
}
