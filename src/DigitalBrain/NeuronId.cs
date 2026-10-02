namespace DigitalBrain;

// The identity of a neuron: a value, not a capability. Carrying a NeuronId conveys which
// neuron, never the power to invoke it — reaching the neuron always goes through a brain.
public readonly record struct NeuronId(string Id)
{
    public override string ToString() => Id;
}
