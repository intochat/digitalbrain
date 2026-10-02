namespace DigitalBrain;

// The formed connection, returned by Watch: holding it is being connected, disposing it severs
// it, and the signals that cross it flow through it. Delivery is at-least-once; neuron-held
// synapses resume from their watermark, and observers dedup through neuron state.
public interface ISynapse : IAsyncDisposable
{
    // The signals crossing this synapse, in publish order, as they arrive — the same facts the
    // observer receives, readable as a stream by whoever holds the connection.
    IAsyncEnumerable<Signal> Signals(CancellationToken cancellationToken = default);

    // Completes when the kernel can no longer honor the connection (severed, holder gone,
    // brain disposing); reading past that point is over, not an error.
    Task Completion { get; }
}
