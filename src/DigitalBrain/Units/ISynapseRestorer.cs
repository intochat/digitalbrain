namespace DigitalBrain.Units;

/// <summary>Restores the loaded source of a state alias a host does not hold, as after a restart, before an activation of the alias fails.</summary>
public interface ISynapseRestorer
{
    /// <summary>Tries to make <paramref name="alias"/> hostable for <paramref name="brain"/>.</summary>
    /// <param name="brain">The brain of the activation.</param>
    /// <param name="alias">The neuron type no class is hosted for.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>True when the alias was restored.</returns>
    Task<bool> TryRestore(BrainId brain, string alias, CancellationToken ct);
}
