using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.UI;

/// <summary>Replaces a named graph with the supplied nodes and edges.</summary>
[GenerateSerializer]
[Alias("ui.render-graph")]
public sealed record RenderGraph(
    CommandId Id,
    [property: Id(0)] string Title,
    [property: Id(1)] IReadOnlyList<GraphNodeState> Nodes,
    [property: Id(2)] IReadOnlyList<GraphEdgeState> Edges) : Command(Id);
