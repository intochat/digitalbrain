namespace DigitalBrain;

/// <summary>
/// The identity of a neuron: a value, not a capability. Carrying a <see cref="NeuronId"/> says
/// which neuron, never grants the power to invoke it — reaching the neuron always goes through
/// a brain, where the kernel scopes and stamps the caller.
/// </summary>
/// <param name="Id">The neuron's address within its brain.</param>
public readonly record struct NeuronId(string Id)
{
    /// <summary>Returns <see cref="Id"/>, so a neuron id reads as its address wherever it is printed.</summary>
    public override string ToString() => Id;
}
