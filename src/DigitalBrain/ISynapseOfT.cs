namespace DigitalBrain;

/// <summary>
/// A synapse carrying one kind of signal, formed by <see cref="Neuron.Watch{T}"/>. The same
/// connection as <see cref="ISynapse"/>, read through the type that matters to the watcher:
/// only signals of <typeparamref name="T"/> cross it.
/// </summary>
public interface ISynapse<out T> : IAsyncDisposable where T : Signal
{
    /// <summary>
    /// The signals of <typeparamref name="T"/> crossing this synapse, in publish order, as
    /// they arrive.
    /// </summary>
    /// <param name="cancellationToken">Stops reading without severing the synapse.</param>
    IAsyncEnumerable<T> Signals(CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes when the kernel can no longer honor the connection — it was severed, its
    /// holder went away, or the brain is disposing. Reading past that point is over, not an
    /// error.
    /// </summary>
    Task Completion { get; }
}
