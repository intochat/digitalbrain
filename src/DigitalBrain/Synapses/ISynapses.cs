using DigitalBrain.Neurons;

namespace DigitalBrain.Kernel;

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

    /// <summary>
    /// Ensures signals whose journal alias is one of <paramref name="signalAliases"/>, published by any other neuron of this brain, reach
    /// this neuron through its one brain-wide synapse; the subscriber needs no reference to the signal types. Calling it again adds aliases.
    /// </summary>
    /// <remarks>
    /// An alias needs no payload type when the edge is made: no unit has to have registered it, and a publisher loaded later is heard
    /// without re-wiring. The type is looked up at delivery; when no loaded unit provides it, a state neuron's alias reactions get the
    /// journal's raw JSON as the <see cref="SignalEnvelope.Payload"/> (typed reactions and <c>[On]</c> methods never see such a signal).
    /// </remarks>
    /// <exception cref="ArgumentException">No alias is given, or an alias is empty.</exception>
    /// <exception cref="InvalidOperationException">This neuron is stateless.</exception>
    Task EnsureBrainWide(IReadOnlyList<string> signalAliases);

    /// <summary>Removes the brain-wide synapse of this neuron; one it does not hold is ignored.</summary>
    Task RemoveBrainWide();
}
