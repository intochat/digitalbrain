namespace DigitalBrain.Abstractions.Brain;

[GenerateSerializer]
[Alias("db.brain-reference")]
public sealed record BrainReference(
    [property: Id(0)] BrainReferenceKind Kind,
    [property: Id(1)] string Type,
    [property: Id(2)] string Name,
    [property: Id(3)] DateTimeOffset LastUsed)
{
    // Tally key: Type is the exact grain-type string (no suffix scheme), so Type alone already
    // separates the kinds -- a neuron and an entity never share a Type value.
    public string Key => $"{Type}/{Name}";
}
