namespace DigitalBrain.Graph;

/// <summary>Groups nodes by module prefix and renders the three levels.</summary>
internal static class Grouping
{
    /// <summary>Assigns every node to a group: a neuron's module prefix (the type up to the first dot), <c>memory</c>, <c>external</c>, or none.</summary>
    public static (GraphGroup[] Groups, int[] Group) Assign(GraphSink sink, BrainId brain)
    {
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        var groups = new List<GraphGroup>();
        var group = new int[sink.Nodes.Count];
        for (var i = 0; i < group.Length; i++)
        {
            var (id, kind, _) = sink.Nodes[i];
            var key = GroupOf(id, kind, brain);
            if (key is null) { group[i] = -1; continue; }
            if (!index.TryGetValue(key, out var g))
            {
                g = groups.Count;
                index[key] = g;
                var groupKind = kind == GraphNodeKind.External
                    ? GraphNodeKind.External
                    : kind is GraphNodeKind.Fact or GraphNodeKind.Entity
                        ? GraphNodeKind.Fact
                        : kind is GraphNodeKind.Task or GraphNodeKind.Step or GraphNodeKind.Run
                            ? GraphNodeKind.Task
                            : GraphNodeKind.Neuron;
                groups.Add(new GraphGroup(key, key, groupKind, 0, false));
            }
            group[i] = g;
            groups[g] = groups[g] with { Size = groups[g].Size + 1 };
        }
        return ([.. groups], group);
    }

    private static string? GroupOf(string id, GraphNodeKind kind, BrainId brain)
    {
        switch (kind)
        {
            case GraphNodeKind.Brain:
            case GraphNodeKind.Contract:
            case GraphNodeKind.Signal:
                return null;
            case GraphNodeKind.External:
                return "external";
            case GraphNodeKind.Fact:
            case GraphNodeKind.Entity:
                return "memory";
            case GraphNodeKind.Task:
            case GraphNodeKind.Step:
            case GraphNodeKind.Run:
                return "tasks";
            default:
                var owner = id;
                var hash = id.IndexOf('#', StringComparison.Ordinal);
                if (hash > 0) { owner = id[..hash]; }
                if (!NeuronId.TryParse(owner, out var neuronId) || neuronId.Brain != brain) { return "other"; }
                var dot = neuronId.Type.IndexOf('.', StringComparison.Ordinal);
                return dot > 0 ? neuronId.Type[..dot] : neuronId.Type;
        }
    }

    /// <summary>One level of the graph.</summary>
    public static GraphSnapshot View(GraphSnapshot g, GraphAnalysis analysis, GraphLevel level, string? focus)
        => level switch
        {
            GraphLevel.Brain => BrainLevel(g),
            GraphLevel.Cluster => ClusterLevel(g, analysis, focus),
            _ => NeuronsLevel(g, focus),
        };

    private static GraphSnapshot BrainLevel(GraphSnapshot g)
    {
        var sink = new GraphSink();
        var map = new int[g.Groups.Length];
        for (var i = 0; i < g.Groups.Length; i++)
        {
            map[i] = sink.Node("group:" + g.Groups[i].Id, g.Groups[i].Kind, g.Groups[i].Label);
        }
        for (var from = 0; from < g.NodeCount; from++)
        {
            if (g.Group[from] < 0) { continue; }
            for (var e = g.RowStart[from]; e < g.RowStart[from + 1]; e++)
            {
                var to = g.Col[e];
                if (g.Group[to] < 0 || g.Group[to] == g.Group[from]) { continue; }
                sink.Edge(map[g.Group[from]], map[g.Group[to]], g.EdgeKinds[e], g.Weights[e]);
            }
        }
        var group = new int[sink.Nodes.Count];
        for (var i = 0; i < group.Length; i++) { group[i] = i; }
        return Csr.Build(g.Revision, sink, g.Groups, group);
    }

    private static GraphSnapshot ClusterLevel(GraphSnapshot g, GraphAnalysis analysis, string? focus)
    {
        var groupIndex = focus is null ? -1 : Array.FindIndex(g.Groups, group => group.Id == focus || "group:" + group.Id == focus);
        var member = new bool[g.NodeCount];
        for (var i = 0; i < g.NodeCount; i++) { member[i] = groupIndex < 0 ? g.Group[i] >= 0 : g.Group[i] == groupIndex; }
        if (groupIndex >= 0 && g.Groups[groupIndex].Id == "tasks")
        {
            for (var from = 0; from < g.NodeCount; from++)
            {
                if (!member[from]) { continue; }
                for (var e = g.RowStart[from]; e < g.RowStart[from + 1]; e++)
                {
                    if (g.EdgeKinds[e] is GraphEdgeKind.WorksOn or GraphEdgeKind.Executes or GraphEdgeKind.Produced)
                    {
                        member[g.Col[e]] = true;
                    }
                }
            }
        }

        var sccSize = new Dictionary<int, int>();
        for (var i = 0; i < g.NodeCount; i++)
        {
            if (member[i]) { sccSize[analysis.Scc[i]] = sccSize.GetValueOrDefault(analysis.Scc[i]) + 1; }
        }

        var sink = new GraphSink();
        var groups = new List<GraphGroup>();
        var groupOf = new List<int>();
        var map = new int[g.NodeCount];
        Array.Fill(map, -1);
        for (var i = 0; i < g.NodeCount; i++)
        {
            if (!member[i]) { continue; }
            var scc = analysis.Scc[i];
            if (sccSize[scc] > 1)
            {
                var id = $"scc:{scc}";
                var before = sink.Nodes.Count;
                map[i] = sink.Node(id, g.Kinds[i], $"{sccSize[scc]} in a cycle");
                if (sink.Nodes.Count > before)
                {
                    groups.Add(new GraphGroup(id, sink.Nodes[map[i]].Label, g.Kinds[i], sccSize[scc], true));
                    groupOf.Add(groups.Count - 1);
                }
            }
            else
            {
                map[i] = sink.Node(g.Ids[i], g.Kinds[i], g.Labels[i]);
                groupOf.Add(-1);
            }
        }
        for (var from = 0; from < g.NodeCount; from++)
        {
            if (map[from] < 0) { continue; }
            for (var e = g.RowStart[from]; e < g.RowStart[from + 1]; e++)
            {
                var to = g.Col[e];
                if (map[to] < 0 || map[to] == map[from]) { continue; }
                sink.Edge(map[from], map[to], g.EdgeKinds[e], g.Weights[e]);
            }
        }
        return Csr.Build(g.Revision, sink, [.. groups], [.. groupOf]);
    }

    private static GraphSnapshot NeuronsLevel(GraphSnapshot g, string? focus)
    {
        var start = focus is null ? -1 : g.IndexOf(focus);
        if (start < 0) { return g; }
        var keep = new HashSet<int>(Walks.Hops(g, start, 1, null));
        for (var e = g.RowStart[start]; e < g.RowStart[start + 1]; e++)
        {
            if (g.EdgeKinds[e] != GraphEdgeKind.Owns) { continue; }
            foreach (var owned in Walks.Hops(g, g.Col[e], 1, null)) { keep.Add(owned); }
        }
        var sink = new GraphSink();
        var map = new int[g.NodeCount];
        Array.Fill(map, -1);
        var groupOf = new List<int>();
        foreach (var i in keep.Order())
        {
            map[i] = sink.Node(g.Ids[i], g.Kinds[i], g.Labels[i]);
            groupOf.Add(g.Group[i]);
        }
        for (var from = 0; from < g.NodeCount; from++)
        {
            if (map[from] < 0) { continue; }
            for (var e = g.RowStart[from]; e < g.RowStart[from + 1]; e++)
            {
                if (map[g.Col[e]] >= 0) { sink.Edge(map[from], map[g.Col[e]], g.EdgeKinds[e], g.Weights[e]); }
            }
        }
        return Csr.Build(g.Revision, sink, g.Groups, [.. groupOf]);
    }
}
