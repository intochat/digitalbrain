namespace DigitalBrain;

public readonly record struct NeuronId(string Kind, string Name)
{
    public override string ToString() => $"{Kind}/{Name}";
}
