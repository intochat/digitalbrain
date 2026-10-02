namespace DigitalBrain;

/// <summary>
/// A typed fact that crosses a synapse. Signals are values: they serialize as pure data on any
/// runtime and carry no capability — turning a signal back into its source neuron goes through
/// <see cref="IDigitalBrain.Get{T}"/> with its <see cref="Publisher"/>.
/// </summary>
public record Signal
{
    /// <summary>
    /// The neuron this signal was published by. Stamped by the kernel at publish — never
    /// supplied by the sender, so provenance cannot be forged; default only for signals born
    /// outside a neuron.
    /// </summary>
    public NeuronId Publisher { get; init; }
}
