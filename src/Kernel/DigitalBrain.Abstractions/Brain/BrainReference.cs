namespace DigitalBrain.Abstractions.Brain;

[GenerateSerializer]
[Alias("db.brain-reference")]
public sealed record BrainReference(
    [property: Id(0)] BrainReferenceKind Kind,
    [property: Id(1)] string Type,
    [property: Id(2)] string Name,
    [property: Id(3)] DateTimeOffset LastUsed)
{
    // Tally key: Type alone already separates the kinds (a neuron "chart" vs an entity "chartentity").
    public string Key => $"{Type}/{Name}";
}
