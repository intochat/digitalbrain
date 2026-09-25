using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Flutter.Graph;

[Alias("graph"), Orleans.Metadata.DefaultGrainType(UIVocabulary.GraphType)]
public interface IGraph : INeuron
{
    Task Render(string title, IReadOnlyList<GraphNode> nodes, IReadOnlyList<GraphEdge> edges);
    Task Emit(GraphActivityPulse pulse);
    [ReadOnly, Alias("read")] Task<GraphState> Read();
}

[GenerateSerializer, Alias("ui.graph-node")]
public sealed record GraphNode([property: Id(0)] string Id, [property: Id(1)] string Label,
    [property: Id(2)] string? IconKey = null);

[GenerateSerializer, Alias("ui.graph-activity-pulse")]
public sealed record GraphActivityPulse(
    [property: Id(0)] string EventId,
    [property: Id(1)] string SourceId,
    [property: Id(2)] string? TargetId,
    [property: Id(3)] string Kind,
    [property: Id(4)] string Status,
    [property: Id(5)] long Sequence);

[GenerateSerializer, Alias("ui.graph-edge")]
public sealed record GraphEdge([property: Id(0)] string From, [property: Id(1)] string To);

[GenerateSerializer, Alias("ui.graph-state")]
public sealed class GraphState
{
    [Id(0)] public string Name { get; set; } = "";
    [Id(1)] public int Version { get; set; }
    [Id(2)] public string Title { get; set; } = "";
    [Id(3)] public List<GraphNode> Nodes { get; set; } = [];
    [Id(4)] public List<GraphEdge> Edges { get; set; } = [];
}
