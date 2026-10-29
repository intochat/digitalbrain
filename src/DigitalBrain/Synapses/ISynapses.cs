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
}
