namespace DigitalBrain.Graph;

/// <summary>Builds and reads compressed sparse row snapshots.</summary>
internal static class Csr
{
    /// <summary>Builds a snapshot from a sink. Duplicate edges (same from, to and kind) collapse into one with summed weight.</summary>
    public static GraphSnapshot Build(long revision, GraphSink sink, GraphGroup[] groups, int[] group)
    {
        var n = sink.Nodes.Count;
        var merged = new Dictionary<(int, int, GraphEdgeKind), float>();
        foreach (var (from, to, kind, weight) in sink.Edges)
        {
            merged[(from, to, kind)] = merged.GetValueOrDefault((from, to, kind)) + weight;
        }
        var ordered = merged.OrderBy(pair => pair.Key.Item1).ThenBy(pair => pair.Key.Item2).ThenBy(pair => pair.Key.Item3).ToArray();
        var rowStart = new int[n + 1];
        foreach (var pair in ordered) { rowStart[pair.Key.Item1 + 1]++; }
        for (var i = 0; i < n; i++) { rowStart[i + 1] += rowStart[i]; }
        var col = new int[ordered.Length];
        var kinds = new GraphEdgeKind[ordered.Length];
        var weights = new float[ordered.Length];
        for (var e = 0; e < ordered.Length; e++)
        {
            col[e] = ordered[e].Key.Item2;
            kinds[e] = ordered[e].Key.Item3;
            weights[e] = ordered[e].Value;
        }
        return new GraphSnapshot(
            revision,
            [.. sink.Nodes.Select(node => node.Id)],
            [.. sink.Nodes.Select(node => node.Kind)],
            [.. sink.Nodes.Select(node => node.Label)],
            group,
            rowStart,
            col,
            kinds,
            weights,
            groups);
    }

    /// <summary>In-edges as row starts and sources, so walks can go against the arrows.</summary>
    public static (int[] RowStart, int[] Source, int[] Edge) Reverse(GraphSnapshot g)
    {
        var n = g.NodeCount;
        var rowStart = new int[n + 1];
        for (var e = 0; e < g.Col.Length; e++) { rowStart[g.Col[e] + 1]++; }
        for (var i = 0; i < n; i++) { rowStart[i + 1] += rowStart[i]; }
        var fill = (int[])rowStart.Clone();
        var source = new int[g.Col.Length];
        var edge = new int[g.Col.Length];
        for (var from = 0; from < n; from++)
        {
            for (var e = g.RowStart[from]; e < g.RowStart[from + 1]; e++)
            {
                var slot = fill[g.Col[e]]++;
                source[slot] = from;
                edge[slot] = e;
            }
        }
        return (rowStart, source, edge);
    }

    /// <summary>A stable hash of the node ids and edges, so an unchanged rebuild does not bump the revision.</summary>
    public static long Hash(GraphSnapshot g)
    {
        var hash = new HashCode();
        foreach (var id in g.Ids) { hash.Add(id, StringComparer.Ordinal); }
        foreach (var kind in g.Kinds) { hash.Add(kind); }
        for (var e = 0; e < g.Col.Length; e++)
        {
            hash.Add(g.Col[e]);
            hash.Add(g.EdgeKinds[e]);
            hash.Add(g.Weights[e]);
        }
        foreach (var start in g.RowStart) { hash.Add(start); }
        return hash.ToHashCode();
    }

    /// <summary>Whether edge <paramref name="e"/> is of one of <paramref name="kinds"/>, or any edge when null.</summary>
    public static bool Allowed(GraphSnapshot g, int e, GraphEdgeKind[]? kinds)
        => kinds is null || Array.IndexOf(kinds, g.EdgeKinds[e]) >= 0;
}
