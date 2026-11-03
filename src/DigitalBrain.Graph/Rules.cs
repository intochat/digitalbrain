using System.Globalization;

namespace DigitalBrain.Graph;

/// <summary>What the analysis flags: call cycles, dead neurons, orphan subscriptions, orphan task steps, stuck runs and hubs.</summary>
internal static class Rules
{
    public const float StuckRunMinutes = 5f;

    private static readonly GraphEdgeKind[] CallKinds = [GraphEdgeKind.Calls];

    public static GraphFinding[] Run(GraphSnapshot g, float[] pageRank)
    {
        var findings = new List<GraphFinding>();
        CallCycles(g, findings);
        DeadAndOrphans(g, findings);
        OrphanTasks(g, findings);
        StuckRuns(g, findings);
        Hubs(g, pageRank, findings);
        return [.. findings];
    }

    // A call goes neuron → contract; the contract is served by the neurons that implement it, so the walk follows Implements in reverse.
    private static void CallCycles(GraphSnapshot g, List<GraphFinding> findings)
    {
        var sink = new GraphSink();
        for (var i = 0; i < g.NodeCount; i++) { sink.Node(g.Ids[i], g.Kinds[i], g.Labels[i]); }
        for (var from = 0; from < g.NodeCount; from++)
        {
            for (var e = g.RowStart[from]; e < g.RowStart[from + 1]; e++)
            {
                if (g.EdgeKinds[e] == GraphEdgeKind.Calls) { sink.Edge(from, g.Col[e], GraphEdgeKind.Calls); }
                else if (g.EdgeKinds[e] == GraphEdgeKind.Implements) { sink.Edge(g.Col[e], from, GraphEdgeKind.Calls); }
            }
        }
        var calls = Csr.Build(g.Revision, sink, [], new int[g.NodeCount]);
        var scc = Scc.Tarjan(calls, CallKinds);
        var members = new Dictionary<int, List<int>>();
        for (var i = 0; i < g.NodeCount; i++)
        {
            if (!members.TryGetValue(scc[i], out var list)) { list = []; members[scc[i]] = list; }
            list.Add(i);
        }
        foreach (var list in members.Values)
        {
            var neurons = list.Where(i => g.Kinds[i] is not GraphNodeKind.Contract).ToArray();
            var cycle = neurons.Length > 1 || (neurons.Length == 1 && SelfLoop(g, neurons[0]));
            if (!cycle) { continue; }
            findings.Add(new GraphFinding("call-cycle", [.. neurons.Select(i => g.Ids[i])],
                string.Create(CultureInfo.InvariantCulture, $"{neurons.Length} neuron(s) call each other in a cycle; a non-reentrant turn would wait on itself.")));
        }
    }

    private static bool SelfLoop(GraphSnapshot g, int i)
    {
        for (var e = g.RowStart[i]; e < g.RowStart[i + 1]; e++)
        {
            if (g.Col[e] == i && g.EdgeKinds[e] == GraphEdgeKind.Calls) { return true; }
        }
        return false;
    }

    private static void DeadAndOrphans(GraphSnapshot g, List<GraphFinding> findings)
    {
        var active = new bool[g.NodeCount];
        for (var from = 0; from < g.NodeCount; from++)
        {
            for (var e = g.RowStart[from]; e < g.RowStart[from + 1]; e++)
            {
                var kind = g.EdgeKinds[e];
                if (kind is GraphEdgeKind.Owns or GraphEdgeKind.Implements) { continue; }
                active[from] = true;
                active[g.Col[e]] = true;
                if (kind == GraphEdgeKind.Subscribes && g.Kinds[g.Col[e]] == GraphNodeKind.External)
                {
                    findings.Add(new GraphFinding("orphan-subscription", [g.Ids[from], g.Ids[g.Col[e]]], "Subscribes to a neuron of another brain that this graph cannot see."));
                }
            }
        }
        for (var i = 0; i < g.NodeCount; i++)
        {
            if (!active[i] && g.Kinds[i] is GraphNodeKind.Neuron or GraphNodeKind.App or GraphNodeKind.Connection)
            {
                findings.Add(new GraphFinding("dead", [g.Ids[i]], "No synapse, call, grant or mention touches this neuron."));
            }
        }
    }

    private static void OrphanTasks(GraphSnapshot g, List<GraphFinding> findings)
    {
        var implemented = new bool[g.NodeCount];
        for (var from = 0; from < g.NodeCount; from++)
        {
            for (var e = g.RowStart[from]; e < g.RowStart[from + 1]; e++)
            {
                if (g.EdgeKinds[e] == GraphEdgeKind.Implements)
                {
                    implemented[g.Col[e]] = true;
                }
            }
        }

        for (var i = 0; i < g.NodeCount; i++)
        {
            if (g.Kinds[i] != GraphNodeKind.Step) { continue; }
            var resolved = false;
            for (var e = g.RowStart[i]; e < g.RowStart[i + 1]; e++)
            {
                if (g.EdgeKinds[e] != GraphEdgeKind.Executes) { continue; }
                var target = g.Col[e];
                if (g.Kinds[target] != GraphNodeKind.Contract || implemented[target])
                {
                    resolved = true;
                    break;
                }
            }
            if (!resolved)
            {
                findings.Add(new GraphFinding("orphan-task", [g.Ids[i]], "Step requires a capability that resolves to no contract in this brain."));
            }
        }
    }

    private static void StuckRuns(GraphSnapshot g, List<GraphFinding> findings)
    {
        for (var i = 0; i < g.NodeCount; i++)
        {
            if (g.Kinds[i] != GraphNodeKind.Run) { continue; }
            var hasTarget = false;
            var stuck = false;
            for (var e = g.RowStart[i]; e < g.RowStart[i + 1]; e++)
            {
                if (g.EdgeKinds[e] is GraphEdgeKind.WorksOn or GraphEdgeKind.Produced)
                {
                    hasTarget = true;
                    if (g.EdgeKinds[e] == GraphEdgeKind.WorksOn && g.Weights[e] >= StuckRunMinutes)
                    {
                        stuck = true;
                    }
                }
            }
            if (stuck || !hasTarget)
            {
                findings.Add(new GraphFinding("stuck-run", [g.Ids[i]], string.Create(CultureInfo.InvariantCulture, $"Run has recorded no turn progress for at least {StuckRunMinutes:0} minutes.")));
            }
        }
    }

    private static void Hubs(GraphSnapshot g, float[] pageRank, List<GraphFinding> findings)
    {
        var count = 0;
        var sum = 0f;
        for (var i = 0; i < g.NodeCount; i++)
        {
            if (g.Kinds[i] is GraphNodeKind.Neuron or GraphNodeKind.App) { count++; sum += pageRank[i]; }
        }
        if (count < 4) { return; }
        var threshold = 3f * sum / count;
        var (inStart, _, _) = Csr.Reverse(g);
        for (var i = 0; i < g.NodeCount; i++)
        {
            var degree = g.RowStart[i + 1] - g.RowStart[i] + inStart[i + 1] - inStart[i];
            if (g.Kinds[i] is GraphNodeKind.Neuron or GraphNodeKind.App && pageRank[i] > threshold && degree >= 6)
            {
                findings.Add(new GraphFinding("hub", [g.Ids[i]], string.Create(CultureInfo.InvariantCulture, $"PageRank {pageRank[i]:0.000} is over three times the mean; many paths go through this neuron.")));
            }
        }
    }
}
