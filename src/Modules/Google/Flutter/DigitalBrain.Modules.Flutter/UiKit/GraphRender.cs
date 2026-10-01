using DigitalBrain.Flutter.Graph;

namespace DigitalBrain.Flutter;

internal sealed record GraphRender(string Title, IReadOnlyList<GraphNode> Nodes, IReadOnlyList<GraphEdge> Edges);
