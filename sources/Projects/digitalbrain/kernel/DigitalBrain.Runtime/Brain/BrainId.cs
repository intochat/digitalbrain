namespace DigitalBrain.Runtime.Brain;

using Orleans;

[GenerateSerializer]
public sealed record BrainId(
    [property: Id(0)] string Value)
{
    public override string ToString() => Value;

    public static implicit operator string(BrainId id) => id.Value;
    public static implicit operator BrainId(string value) => new(value);
}
