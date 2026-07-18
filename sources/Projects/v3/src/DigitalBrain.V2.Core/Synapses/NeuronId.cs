namespace DigitalBrain.V2.Core.Synapses;

[GenerateSerializer]
public readonly record struct NeuronId(
    [property: Id(0)] string Type,
    [property: Id(1)] string Key)
{
    public static readonly NeuronId None = new(string.Empty, string.Empty);
    public bool IsNone => string.IsNullOrEmpty(Type);
    public override string ToString() => IsNone ? "<none>" : $"{Type}/{Key}";
}
