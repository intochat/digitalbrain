namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias("ui.graph-state")]
public sealed record GraphState(
    [property: Id(0)] string Title,
    [property: Id(1)] IReadOnlyList<GraphNodeState> Nodes,
    [property: Id(2)] IReadOnlyList<GraphEdgeState> Edges);
