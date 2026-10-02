namespace DigitalBrain;

public readonly record struct NeuronId(string Id)
{
    public override string ToString() => Id;
}
