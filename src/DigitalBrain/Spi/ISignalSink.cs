namespace DigitalBrain.Spi;

/// <summary>
/// A runtime service that takes delivered signals itself instead of through <c>[On]</c> methods: the session neuron queues
/// them into batches, the brain listener neuron hands them to silo-local handlers. Module neurons never implement it.
/// </summary>
internal interface ISignalSink
{
    /// <summary>Takes one signal in order. Throwing leaves the cursor before it; the next reference retries.</summary>
    Task Deliver(JournaledSignal item);
}
