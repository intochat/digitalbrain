namespace DigitalBrain.Spi;

/// <summary>What a host calls on a state neuron to run its synapse functions.</summary>
public interface ISynapseReceiver
{
    /// <summary>Runs the reaction synapses for <paramref name="signal"/> in registration order; after a save the views run.</summary>
    /// <param name="signal">The arriving signal.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>A task that completes when every reaction has run.</returns>
    Task Receive(Signal signal, CancellationToken ct);

    /// <summary>Runs the reaction synapses for the journal entry <paramref name="journaled"/>; alias reactions get its trace and causation ids.</summary>
    /// <remarks>
    /// Runtimes call this overload. The default forwards to <see cref="Receive(Signal, CancellationToken)"/> and drops the entry's ids, so a
    /// receiver with alias reactions (one that builds a <see cref="SignalEnvelope"/>) must override it.
    /// </remarks>
    /// <param name="journaled">The arriving journal entry.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>A task that completes when every reaction has run.</returns>
    Task Receive(JournaledSignal journaled, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(journaled);
        return Receive(journaled.Signal, ct);
    }
}
