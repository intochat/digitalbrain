namespace DigitalBrain;

// A neuron is a stateful, addressable capability; all state lives in neurons, never in a
// process. A neuron publishes signals only in its own name, and is where synapses form:
// watching a neuron connects an observer to it.
public interface INeuron
{
    // Forms a synapse between this neuron and the observer; every signal published from now on
    // (or, for a neuron-held observer, from its watermark) crosses it until it is severed.
    Task<ISynapse> Watch(INeuronObserver observer);

    // Severs this observer's synapse. Disposing the synapse does the same; Unwatch exists so a
    // connection can be severed by anyone cleaning up — an app uninstalling its script must not
    // need the handle, and severing must be safe while signals are being delivered.
    Task Unwatch(INeuronObserver observer);
}
