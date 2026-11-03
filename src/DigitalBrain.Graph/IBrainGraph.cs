using System.ComponentModel;
using DigitalBrain.Contracts;

namespace DigitalBrain.Graph;

/// <summary>The brain as one graph, derived from existing state. One neuron per brain, keyed <c>{brain}/brain.graph/default</c>.</summary>
[NeuronAlias("brain.graph"), DefaultNeuronType("brain.graph")]
[Description("The brain as one graph: neurons, apps, contracts, signals, grants and memory, derived from existing state.")]
public interface IBrainGraph
{
    /// <summary>The graph at one level. <paramref name="focus"/> is a group id at the cluster level or a node id at the neurons level.</summary>
    Task<GraphSnapshot> Snapshot(GraphLevel level, string? focus);

    /// <summary>Components, cycles, communities, centrality and findings for the current revision.</summary>
    Task<GraphAnalysis> Analysis();

    /// <summary>The ids within <paramref name="hops"/> of <paramref name="id"/> in either direction, over <paramref name="kinds"/> or every edge kind when null.</summary>
    Task<string[]> Neighbourhood(string id, int hops, GraphEdgeKind[]? kinds);

    /// <summary>The shortest path from <paramref name="from"/> to <paramref name="to"/> in either direction, or none; at most <paramref name="maxHops"/> long.</summary>
    Task<GraphPath[]> Paths(string from, string to, int maxHops);

    /// <summary>Rebuilds the graph from its sources now and returns the revision.</summary>
    Task<long> Rebuild();
}

/// <summary>A host-side source of graph nodes and edges beyond what the kernel observes: app shapes, memory facts, grants.</summary>
public interface IGraphSource
{
    /// <summary>Adds the nodes and edges this source knows about <paramref name="brain"/>.</summary>
    Task Contribute(BrainId brain, GraphSink sink, CancellationToken cancellationToken);
}

/// <summary>Where sources put nodes and edges while a snapshot is built. Node ids are deduplicated.</summary>
public sealed class GraphSink
{
    private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);

    internal List<(string Id, GraphNodeKind Kind, string Label)> Nodes { get; } = [];

    internal List<(int From, int To, GraphEdgeKind Kind, float Weight)> Edges { get; } = [];

    /// <summary>Adds or finds the node <paramref name="id"/> and returns its index. The first kind and label win unless a source refines a generic neuron node.</summary>
    public int Node(string id, GraphNodeKind kind, string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (_index.TryGetValue(id, out var index))
        {
            var current = Nodes[index];
            var nextKind = current.Kind == GraphNodeKind.Neuron && kind != GraphNodeKind.Neuron ? kind : current.Kind;
            var nextLabel = !string.IsNullOrWhiteSpace(label) && label != id && current.Label == GraphBuilder.LabelOf(id) ? label : current.Label;
            if (nextKind != current.Kind || nextLabel != current.Label)
            {
                Nodes[index] = (id, nextKind, nextLabel);
            }
            return index;
        }
        index = Nodes.Count;
        _index[id] = index;
        Nodes.Add((id, kind, label ?? id));
        return index;
    }

    /// <summary>The index of <paramref name="id"/>, or -1.</summary>
    public int IndexOf(string id) => _index.TryGetValue(id, out var index) ? index : -1;

    /// <summary>Adds an edge between two node indexes.</summary>
    public void Edge(int from, int to, GraphEdgeKind kind, float weight = 1f)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(from);
        ArgumentOutOfRangeException.ThrowIfNegative(to);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(from, Nodes.Count);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(to, Nodes.Count);
        Edges.Add((from, to, kind, weight));
    }
}
