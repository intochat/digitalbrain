namespace DigitalBrain.Spi;

/// <summary>
/// The synapses a neuron holds as a subscriber. A synapse is durable: it survives restarts and is declared again harmlessly.
/// Every call is idempotent. The source authorizes the subscriber when the synapse is created (<c>AdmitSynapse</c>).
/// A subscriber never subscribes to itself.
/// </summary>
public interface ISynapses
{
    /// <summary>Ensures signals of type <typeparamref name="TSignal"/> published by <paramref name="from"/> reach this neuron.</summary>
    /// <exception cref="ArgumentException"><paramref name="from"/> is this neuron.</exception>
    /// <exception cref="UnauthorizedAccessException">The source refuses this neuron.</exception>
    Task Ensure<TSignal>(NeuronId from) where TSignal : Signal;

    /// <summary>Ensures signals of each of <paramref name="signals"/> published by <paramref name="from"/> reach this neuron.</summary>
    /// <exception cref="ArgumentException"><paramref name="from"/> is this neuron, or a type is not a signal.</exception>
    /// <exception cref="UnauthorizedAccessException">The source refuses this neuron.</exception>
    Task Ensure(NeuronId from, params Type[] signals);

    /// <summary>Removes every synapse from <paramref name="from"/> to this neuron.</summary>
    Task Remove(NeuronId from);

    /// <summary>
    /// Ensures signals of each of <paramref name="signals"/> published by any other neuron of this brain reach this neuron: one
    /// brain-wide synapse, read in the brain's journal order from where it was created. Calling it again adds signal types.
    /// </summary>
    /// <exception cref="ArgumentException">No signal type is given, or a type is not a signal.</exception>
    /// <exception cref="InvalidOperationException">This neuron is stateless.</exception>
    Task EnsureBrainWide(params Type[] signals);

    /// <summary>Removes the brain-wide synapse of this neuron; one it does not hold is ignored.</summary>
    Task RemoveBrainWide();
}
