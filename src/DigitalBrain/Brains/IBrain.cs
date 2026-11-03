using DigitalBrain.Contracts;

namespace DigitalBrain.Brains;

/// <summary>
/// The brain's first neuron. It holds the brain's own state, publishes the brain's lifecycle
/// signals, and is reached by <see cref="Key(string)"/>.
/// </summary>
/// <remarks>
/// A plain neuron with the brain's singleton key <c>{brain}/main</c>.
/// Creating a brain is the platform's act: <see cref="Create"/> is refused for any other caller, and is idempotent for the platform.
/// </remarks>
[NeuronAlias("brain"), DefaultNeuronType(Type)]
public interface IBrain
{
    /// <summary>The brain neuron's runtime type.</summary>
    const string Type = "brain";

    /// <summary>The full key of <paramref name="brainId"/>'s brain neuron.</summary>
    static string Key(string brainId) => $"{brainId}/main";

    /// <summary>
    /// Brings the brain into existence for <paramref name="owner"/>. Publishes <see cref="Signals.Created"/>
    /// once and awaits its handlers, so the caller receives a brain that is already provisioned.
    /// Calling it on a created brain changes nothing.
    /// </summary>
    Task Create(Owner owner);

    /// <summary>The brain's state as persisted.</summary>
    /// <exception cref="InvalidOperationException">The brain has not been created.</exception>
    [ReadOnlyCall] Task<BrainState> Read();
}
