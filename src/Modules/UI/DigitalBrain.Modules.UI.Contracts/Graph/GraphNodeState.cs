namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias("ui.graph-node")]
public sealed record GraphNodeState(
    [property: Id(0)] string Id,
    [property: Id(1)] string Label,
    [property: Id(2)] string Kind = GraphNodeKinds.Leaf,
    [property: Id(3)] string? Cluster = null);
