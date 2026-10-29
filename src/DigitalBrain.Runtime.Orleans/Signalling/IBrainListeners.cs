namespace DigitalBrain.Runtime.Orleans.Signalling;

/// <summary>
/// Silo-local typed listeners on a brain's signals (04 D-02, R4-03; replaces os <c>LocalSignalHub</c>, RESEARCH Pitfall 8). A
/// per-brain listener neuron <c>brain/runtime.listener/brain</c> owns one brain-wide synapse and hands each wanted signal, in
/// brain order, to the handlers registered on the silo where it runs. That neuron is brain-affine, so register on the silo that
/// hosts the brain's neurons: handlers on other silos receive nothing. Resolve it from the silo's services; neurons and clients
/// never reach it.
/// </summary>
public interface IBrainListeners
{
    /// <summary>
    /// Calls <paramref name="handler"/> for every <typeparamref name="TSignal"/> saved in <paramref name="brain"/> from now on, once
    /// each, in brain order. A handler that throws is logged (<c>BrainListenerFailed</c>) and does not stop the others.
    /// </summary>
    /// <returns>Disposing it stops delivery to this handler.</returns>
    /// <exception cref="ArgumentException"><typeparamref name="TSignal"/> has no registered <c>[Signal]</c> alias.</exception>
    Task<IAsyncDisposable> Listen<TSignal>(BrainId brain, Func<TSignal, CancellationToken, Task> handler) where TSignal : Signal;
}
