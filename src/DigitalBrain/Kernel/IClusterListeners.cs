namespace DigitalBrain.Kernel;

/// <summary>
/// Host-level listeners on the signals of every brain (04-19, R4-19): a host (not a neuron) resolves it from the host's services
/// and hears each matching <c>[Signal]</c> any neuron of any brain saves or publishes from now on, after the append is durable.
/// Delivery is in-memory and best effort (no replay, no cursor): a listener that must not miss a signal holds a synapse instead.
/// Registrations live as long as the host; register at host start so a restarted host listens again.
/// </summary>
public interface IClusterListeners
{
    /// <summary>
    /// Calls <paramref name="handler"/> with the publisher and the signal for every <typeparamref name="TSignal"/> appended in any
    /// brain from now on, in publish order per host. Idempotent per <paramref name="listener"/> and <typeparamref name="TSignal"/>:
    /// a second call with the same pair replaces the handler instead of adding one. A handler that throws is logged and does not
    /// stop the others.
    /// </summary>
    /// <returns>Disposing it stops delivery to this handler (a no-op once the handler was replaced).</returns>
    /// <exception cref="ArgumentException"><typeparamref name="TSignal"/> has no registered <c>[Signal]</c> alias, or <paramref name="listener"/> is blank.</exception>
    Task<IAsyncDisposable> Listen<TSignal>(string listener, Func<NeuronId, TSignal, CancellationToken, Task> handler) where TSignal : Signal;
}
