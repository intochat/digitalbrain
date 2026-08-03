namespace DigitalBrain;

public readonly record struct NeuronId(string Type, string Name)
{
    public override string ToString() => $"{Type}:{Name}";
}
