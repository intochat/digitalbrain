namespace DigitalBrain.Graph;

/// <summary>Weakly connected components by union-find.</summary>
internal static class Components
{
    public static int[] Weak(GraphSnapshot g)
    {
        var parent = new int[g.NodeCount];
        for (var i = 0; i < parent.Length; i++) { parent[i] = i; }
        for (var from = 0; from < g.NodeCount; from++)
        {
            for (var e = g.RowStart[from]; e < g.RowStart[from + 1]; e++) { Union(parent, from, g.Col[e]); }
        }
        var label = new int[parent.Length];
        var next = 0;
        var seen = new Dictionary<int, int>();
        for (var i = 0; i < parent.Length; i++)
        {
            var root = Find(parent, i);
            if (!seen.TryGetValue(root, out var id)) { id = next++; seen[root] = id; }
            label[i] = id;
        }
        return label;
    }

    private static int Find(int[] parent, int i)
    {
        while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; }
        return i;
    }

    private static void Union(int[] parent, int a, int b)
    {
        a = Find(parent, a);
        b = Find(parent, b);
        if (a != b) { parent[Math.Max(a, b)] = Math.Min(a, b); }
    }
}

/// <summary>Strongly connected components by iterative Tarjan, over the edge kinds given or every edge.</summary>
internal static class Scc
{
    public static int[] Tarjan(GraphSnapshot g, GraphEdgeKind[]? kinds = null)
    {
        var n = g.NodeCount;
        var index = new int[n];
        var low = new int[n];
        var onStack = new bool[n];
        var component = new int[n];
        Array.Fill(index, -1);
        Array.Fill(component, -1);
        var stack = new Stack<int>();
        var work = new Stack<(int Node, int Edge)>();
        var next = 0;
        var count = 0;
        for (var start = 0; start < n; start++)
        {
            if (index[start] >= 0) { continue; }
            work.Push((start, g.RowStart[start]));
            index[start] = low[start] = next++;
            stack.Push(start);
            onStack[start] = true;
            while (work.Count > 0)
            {
                var (v, e) = work.Pop();
                var end = g.RowStart[v + 1];
                var descended = false;
                while (e < end)
                {
                    var w = g.Col[e];
                    var current = e++;
                    if (!Csr.Allowed(g, current, kinds)) { continue; }
                    if (index[w] < 0)
                    {
                        work.Push((v, e));
                        work.Push((w, g.RowStart[w]));
                        index[w] = low[w] = next++;
                        stack.Push(w);
                        onStack[w] = true;
                        descended = true;
                        break;
                    }
                    if (onStack[w]) { low[v] = Math.Min(low[v], index[w]); }
                }
                if (descended) { continue; }
                if (low[v] == index[v])
                {
                    int w;
                    do
                    {
                        w = stack.Pop();
                        onStack[w] = false;
                        component[w] = count;
                    }
                    while (w != v);
                    count++;
                }
                if (work.Count > 0)
                {
                    var parent = work.Peek().Node;
                    low[parent] = Math.Min(low[parent], low[v]);
                }
            }
        }
        return component;
    }
}

/// <summary>PageRank with a uniform or a personal teleport vector. Dangling mass follows the teleport vector.</summary>
internal static class PageRank
{
    public static float[] Compute(GraphSnapshot g, float[]? teleport = null, float damping = 0.85f, int iterations = 20)
    {
        var n = g.NodeCount;
        var rank = new float[n];
        if (n == 0) { return rank; }
        var tele = new float[n];
        var sum = 0f;
        if (teleport is not null)
        {
            for (var i = 0; i < n; i++) { tele[i] = Math.Max(0f, teleport[i]); sum += tele[i]; }
        }
        if (sum <= 0f) { Array.Fill(tele, 1f / n); }
        else { for (var i = 0; i < n; i++) { tele[i] /= sum; } }
        Array.Copy(tele, rank, n);
        var outWeight = new float[n];
        for (var from = 0; from < n; from++)
        {
            for (var e = g.RowStart[from]; e < g.RowStart[from + 1]; e++) { outWeight[from] += g.Weights[e]; }
        }
        var next = new float[n];
        for (var it = 0; it < iterations; it++)
        {
            Array.Clear(next);
            var dangling = 0f;
            for (var from = 0; from < n; from++)
            {
                if (outWeight[from] <= 0f) { dangling += rank[from]; continue; }
                var share = rank[from] / outWeight[from];
                for (var e = g.RowStart[from]; e < g.RowStart[from + 1]; e++) { next[g.Col[e]] += share * g.Weights[e]; }
            }
            for (var i = 0; i < n; i++) { next[i] = damping * (next[i] + dangling * tele[i]) + (1f - damping) * tele[i]; }
            (rank, next) = (next, rank);
        }
        return rank;
    }
}

/// <summary>Hub and authority scores by HITS, normalized to unit sum.</summary>
internal static class Hits
{
    public static (float[] Hub, float[] Authority) Compute(GraphSnapshot g, int iterations = 20)
    {
        var n = g.NodeCount;
        var hub = new float[n];
        var authority = new float[n];
        if (n == 0) { return (hub, authority); }
        Array.Fill(hub, 1f / n);
        var (inStart, inSource, _) = Csr.Reverse(g);
        for (var it = 0; it < iterations; it++)
        {
            for (var i = 0; i < n; i++)
            {
                var a = 0f;
                for (var e = inStart[i]; e < inStart[i + 1]; e++) { a += hub[inSource[e]]; }
                authority[i] = a;
            }
            Normalize(authority);
            for (var i = 0; i < n; i++)
            {
                var h = 0f;
                for (var e = g.RowStart[i]; e < g.RowStart[i + 1]; e++) { h += authority[g.Col[e]]; }
                hub[i] = h;
            }
            Normalize(hub);
        }
        return (hub, authority);
    }

    private static void Normalize(float[] values)
    {
        var sum = 0f;
        foreach (var value in values) { sum += value; }
        if (sum <= 0f) { return; }
        for (var i = 0; i < values.Length; i++) { values[i] /= sum; }
    }
}

/// <summary>Breadth-first walks in either direction.</summary>
internal static class Walks
{
    /// <summary>Every node within <paramref name="hops"/> of <paramref name="start"/>, start included, over the allowed kinds.</summary>
    public static int[] Hops(GraphSnapshot g, int start, int hops, GraphEdgeKind[]? kinds)
    {
        var (inStart, inSource, inEdge) = Csr.Reverse(g);
        var distance = new int[g.NodeCount];
        Array.Fill(distance, -1);
        var queue = new Queue<int>();
        distance[start] = 0;
        queue.Enqueue(start);
        var result = new List<int> { start };
        while (queue.Count > 0)
        {
            var v = queue.Dequeue();
            if (distance[v] >= hops) { continue; }
            for (var e = g.RowStart[v]; e < g.RowStart[v + 1]; e++)
            {
                if (Csr.Allowed(g, e, kinds)) { Visit(g.Col[e], v); }
            }
            for (var e = inStart[v]; e < inStart[v + 1]; e++)
            {
                if (Csr.Allowed(g, inEdge[e], kinds)) { Visit(inSource[e], v); }
            }
        }
        return [.. result];

        void Visit(int w, int v)
        {
            if (distance[w] >= 0) { return; }
            distance[w] = distance[v] + 1;
            queue.Enqueue(w);
            result.Add(w);
        }
    }

    /// <summary>The shortest path from <paramref name="from"/> to <paramref name="to"/> ignoring direction, or null; at most <paramref name="maxHops"/> edges.</summary>
    public static GraphPath? Shortest(GraphSnapshot g, int from, int to, int maxHops, GraphEdgeKind[]? kinds)
    {
        if (from == to) { return new GraphPath([g.Ids[from]], []); }
        var (inStart, inSource, inEdge) = Csr.Reverse(g);
        var previous = new int[g.NodeCount];
        var via = new int[g.NodeCount];
        var distance = new int[g.NodeCount];
        Array.Fill(previous, -1);
        Array.Fill(distance, -1);
        var queue = new Queue<int>();
        distance[from] = 0;
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            var v = queue.Dequeue();
            if (distance[v] >= maxHops) { continue; }
            for (var e = g.RowStart[v]; e < g.RowStart[v + 1]; e++)
            {
                if (Csr.Allowed(g, e, kinds) && Reach(g.Col[e], v, e)) { return Trace(); }
            }
            for (var e = inStart[v]; e < inStart[v + 1]; e++)
            {
                if (Csr.Allowed(g, inEdge[e], kinds) && Reach(inSource[e], v, inEdge[e])) { return Trace(); }
            }
        }
        return null;

        bool Reach(int w, int v, int edge)
        {
            if (distance[w] >= 0) { return false; }
            distance[w] = distance[v] + 1;
            previous[w] = v;
            via[w] = edge;
            queue.Enqueue(w);
            return w == to;
        }

        GraphPath Trace()
        {
            var nodes = new List<string>();
            var edges = new List<GraphEdgeKind>();
            for (var v = to; v != -1; v = previous[v])
            {
                nodes.Add(g.Ids[v]);
                if (previous[v] != -1) { edges.Add(g.EdgeKinds[via[v]]); }
            }
            nodes.Reverse();
            edges.Reverse();
            return new GraphPath([.. nodes], [.. edges]);
        }
    }
}

/// <summary>Communities by deterministic label propagation over the undirected graph.</summary>
internal static class Communities
{
    public static int[] LabelPropagation(GraphSnapshot g, int iterations = 10)
    {
        var n = g.NodeCount;
        var label = new int[n];
        for (var i = 0; i < n; i++) { label[i] = i; }
        var (inStart, inSource, _) = Csr.Reverse(g);
        var votes = new Dictionary<int, float>();
        for (var it = 0; it < iterations; it++)
        {
            var changed = false;
            for (var v = 0; v < n; v++)
            {
                votes.Clear();
                for (var e = g.RowStart[v]; e < g.RowStart[v + 1]; e++) { votes[label[g.Col[e]]] = votes.GetValueOrDefault(label[g.Col[e]]) + g.Weights[e]; }
                for (var e = inStart[v]; e < inStart[v + 1]; e++) { votes[label[inSource[e]]] = votes.GetValueOrDefault(label[inSource[e]]) + 1f; }
                if (votes.Count == 0) { continue; }
                var best = label[v];
                var bestVote = votes.GetValueOrDefault(best);
                foreach (var (candidate, vote) in votes)
                {
                    if (vote > bestVote || vote == bestVote && candidate < best) { best = candidate; bestVote = vote; }
                }
                if (best != label[v]) { label[v] = best; changed = true; }
            }
            if (!changed) { break; }
        }
        var dense = new Dictionary<int, int>();
        for (var i = 0; i < n; i++)
        {
            if (!dense.TryGetValue(label[i], out var id)) { id = dense.Count; dense[label[i]] = id; }
            label[i] = id;
        }
        return label;
    }
}
