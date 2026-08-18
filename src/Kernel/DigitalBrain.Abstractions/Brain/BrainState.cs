namespace DigitalBrain.Abstractions.Brain;

[GenerateSerializer]
[Alias("db.brain-state")]
public sealed record BrainState(
    [property: Id(0)] IReadOnlyList<BrainReference> Nodes,
    [property: Id(1)] IReadOnlyList<Connection> Connections,
    [property: Id(2)] IReadOnlyList<BrainContext> Contexts,
    [property: Id(3)] string ActiveContext)
{
    public const string DefaultContext = "default";
    public const int MaximumContexts = 32;
}
