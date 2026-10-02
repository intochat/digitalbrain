namespace DigitalBrain;

/// <summary>
/// The formed connection between a neuron and an observer, returned by
/// <see cref="INeuron.Watch"/>. Holding it is being connected; disposing it severs the
/// connection; the signals that cross it flow through it. Push (the observer's callbacks) and
/// pull (<see cref="Signals"/>) are the same synapse, not two mechanisms.
/// </summary>
public interface ISynapse : IAsyncDisposable
{
    /// <summary>
    /// The signals crossing this synapse, in publish order, as they arrive — the same facts the
    /// observer receives, readable as a stream by whoever holds the connection.
    /// </summary>
    /// <param name="cancellationToken">Stops reading without severing the synapse.</param>
    IAsyncEnumerable<Signal> Signals(CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes when the kernel can no longer honor the connection — it was severed, its
    /// holder went away, or the brain is disposing. Reading past that point is over, not an
    /// error.
    /// </summary>
    Task Completion { get; }
}
