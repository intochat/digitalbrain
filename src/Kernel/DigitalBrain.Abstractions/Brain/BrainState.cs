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
    public const int MaximumNodes = 256;
    // Connect refuses beyond the cap instead of evicting: a wire that silently vanished
    // would break its delivery promise.
    public const int MaximumConnections = 128;
}
