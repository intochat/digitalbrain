namespace DigitalBrain.Graph;

/// <summary>
/// The graph algorithms for a host that holds its own small graph (a memory store ranking its facts) and must not call the
/// brain graph neuron from inside a turn. Build a snapshot from a <see cref="GraphSink"/>, then rank and walk it.
/// </summary>
public static class GraphCompute
{
    /// <summary>A snapshot of the sink's nodes and edges, with no groups.</summary>
    public static GraphSnapshot Build(GraphSink sink, long revision = 0)
    {
        ArgumentNullException.ThrowIfNull(sink);
        var group = new int[sink.Nodes.Count];
        Array.Fill(group, -1);
        return Csr.Build(revision, sink, [], group);
    }

    /// <summary>PageRank with <paramref name="teleport"/> as the personal vector (seed weights by node index), or uniform when null.</summary>
    public static float[] PageRank(GraphSnapshot graph, float[]? teleport = null, float damping = 0.85f, int iterations = 20)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (teleport is not null && teleport.Length != graph.NodeCount) { throw new ArgumentException("The teleport vector has one weight per node.", nameof(teleport)); }
        return Graph.PageRank.Compute(graph, teleport, damping, iterations);
    }

    /// <summary>The shortest path between two node indexes ignoring direction, at most <paramref name="maxHops"/> long, or null.</summary>
    public static GraphPath? ShortestPath(GraphSnapshot graph, int from, int to, int maxHops = 3)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentOutOfRangeException.ThrowIfNegative(from);
        ArgumentOutOfRangeException.ThrowIfNegative(to);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(from, graph.NodeCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(to, graph.NodeCount);
        return Walks.Shortest(graph, from, to, Math.Max(1, maxHops), null);
    }

    /// <summary>Every node index within <paramref name="hops"/> of <paramref name="start"/> in either direction, start included.</summary>
    public static int[] Neighbourhood(GraphSnapshot graph, int start, int hops)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(start, graph.NodeCount);
        return Walks.Hops(graph, start, Math.Max(0, hops), null);
    }
}
