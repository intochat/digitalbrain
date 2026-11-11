using DigitalBrain.Neurons;

namespace DigitalBrain.Graph.Tests.Unit;

public sealed class AlgorithmFacts
{
    private static GraphSnapshot Graph(int nodes, params (int From, int To)[] edges)
    {
        var sink = new GraphSink();
        for (var i = 0; i < nodes; i++) { sink.Node("n" + i, GraphNodeKind.Neuron, "n" + i); }
        foreach (var (from, to) in edges) { sink.Edge(from, to, GraphEdgeKind.Calls); }
        return Csr.Build(1, sink, [], new int[nodes].Select(_ => -1).ToArray());
    }

    [Fact]
    public void Csr_orders_edges_by_source_and_merges_duplicates()
    {
        var sink = new GraphSink();
        sink.Node("a", GraphNodeKind.Neuron, "a");
        sink.Node("b", GraphNodeKind.Neuron, "b");
        sink.Edge(1, 0, GraphEdgeKind.Subscribes);
        sink.Edge(0, 1, GraphEdgeKind.Calls, 2f);
        sink.Edge(0, 1, GraphEdgeKind.Calls, 3f);
        var g = Csr.Build(1, sink, [], [-1, -1]);

        Assert.Equal([0, 1, 2], g.RowStart);
        Assert.Equal([1, 0], g.Col);
        Assert.Equal([5f, 1f], g.Weights);
        Assert.Equal(2, g.EdgeCount);
    }

    [Fact]
    public void Tarjan_finds_a_three_cycle_and_its_tail()
    {
        var g = Graph(4, (0, 1), (1, 2), (2, 0), (2, 3));
        var scc = Scc.Tarjan(g);

        Assert.Equal(scc[0], scc[1]);
        Assert.Equal(scc[1], scc[2]);
        Assert.NotEqual(scc[2], scc[3]);
    }

    [Fact]
    public void Tarjan_respects_the_edge_kind_filter()
    {
        var sink = new GraphSink();
        sink.Node("a", GraphNodeKind.Neuron, "a");
        sink.Node("b", GraphNodeKind.Neuron, "b");
        sink.Edge(0, 1, GraphEdgeKind.Calls);
        sink.Edge(1, 0, GraphEdgeKind.Subscribes);
        var g = Csr.Build(1, sink, [], [-1, -1]);

        Assert.Equal(Scc.Tarjan(g)[0], Scc.Tarjan(g)[1]);
        Assert.NotEqual(Scc.Tarjan(g, [GraphEdgeKind.Calls])[0], Scc.Tarjan(g, [GraphEdgeKind.Calls])[1]);
    }

    [Fact]
    public void Weak_components_ignore_direction()
    {
        var g = Graph(5, (0, 1), (2, 1), (3, 4));
        var component = Components.Weak(g);

        Assert.Equal(component[0], component[1]);
        Assert.Equal(component[1], component[2]);
        Assert.Equal(component[3], component[4]);
        Assert.NotEqual(component[0], component[3]);
    }

    [Fact]
    public void PageRank_ranks_the_centre_of_a_star_highest()
    {
        var g = Graph(5, (1, 0), (2, 0), (3, 0), (4, 0));
        var rank = PageRank.Compute(g);

        Assert.Equal(0, Array.IndexOf(rank, rank.Max()));
        Assert.InRange(rank.Sum(), 0.99f, 1.01f);
    }

    [Fact]
    public void Personalized_PageRank_favours_the_seed_and_its_neighbours()
    {
        var g = Graph(6, (0, 1), (1, 2), (2, 3), (3, 4), (4, 5), (5, 0));
        var seeds = new float[6];
        seeds[3] = 1f;
        var rank = PageRank.Compute(g, seeds);

        Assert.True(rank[3] > rank[0]);
        Assert.True(rank[4] > rank[1]);
    }

    [Fact]
    public void Hits_gives_authority_to_the_pointed_at_node()
    {
        var g = Graph(4, (1, 0), (2, 0), (3, 0), (1, 2));
        var (hub, authority) = Hits.Compute(g);

        Assert.Equal(0, Array.IndexOf(authority, authority.Max()));
        Assert.Equal(1, Array.IndexOf(hub, hub.Max()));
    }

    [Fact]
    public void Shortest_path_goes_against_arrows_and_records_edge_kinds()
    {
        var g = Graph(4, (0, 1), (2, 1), (2, 3));
        var path = Walks.Shortest(g, 0, 3, 5, null);

        Assert.NotNull(path);
        Assert.Equal(["n0", "n1", "n2", "n3"], path.Nodes);
        Assert.Equal(3, path.Edges.Length);
        Assert.Null(Walks.Shortest(g, 0, 3, 2, null));
    }

    [Fact]
    public void Hops_returns_the_bounded_neighbourhood()
    {
        var g = Graph(5, (0, 1), (1, 2), (2, 3), (3, 4));

        Assert.Equal([0, 1, 2, 3, 4], Walks.Hops(g, 2, 2, null).Order());
        Assert.Equal([1, 2, 3], Walks.Hops(g, 2, 1, null).Order());
    }

    [Fact]
    public void Label_propagation_separates_two_dense_clusters()
    {
        var g = Graph(6, (0, 1), (1, 2), (2, 0), (3, 4), (4, 5), (5, 3), (2, 3));
        var community = Communities.LabelPropagation(g);

        Assert.Equal(community[0], community[1]);
        Assert.Equal(community[1], community[2]);
        Assert.Equal(community[3], community[4]);
        Assert.Equal(community[4], community[5]);
    }

    [Fact]
    public void Rules_flag_call_cycles_dead_nodes_and_hubs()
    {
        var sink = new GraphSink();
        var a = sink.Node("b/x/a", GraphNodeKind.Neuron, "a");
        var b = sink.Node("b/x/b", GraphNodeKind.Neuron, "b");
        var dead = sink.Node("b/x/dead", GraphNodeKind.Neuron, "dead");
        var hub = sink.Node("b/x/hub", GraphNodeKind.Neuron, "hub");
        sink.Edge(a, b, GraphEdgeKind.Calls);
        sink.Edge(b, a, GraphEdgeKind.Calls);
        for (var i = 0; i < 8; i++)
        {
            var leaf = sink.Node("b/x/leaf" + i, GraphNodeKind.Neuron, "leaf");
            sink.Edge(leaf, hub, GraphEdgeKind.Subscribes);
            sink.Edge(hub, leaf, GraphEdgeKind.Calls);
        }
        var g = Csr.Build(1, sink, [], new int[sink.Nodes.Count].Select(_ => -1).ToArray());
        var findings = Rules.Run(g, PageRank.Compute(g));

        Assert.Contains(findings, f => f.Rule == "call-cycle" && f.Nodes.Length == 2);
        Assert.Contains(findings, f => f.Rule == "dead" && f.Nodes.Single() == g.Ids[dead]);
        Assert.Contains(findings, f => f.Rule == "hub" && f.Nodes.Single() == g.Ids[hub]);
    }

    [Fact]
    public void Rules_flag_orphan_task_steps_and_stuck_runs()
    {
        var sink = new GraphSink();
        var task = sink.Node("b/tasks.task/t1", GraphNodeKind.Task, "Deploy");
        var stepOk = sink.Node("b/tasks.task/t1#step/0", GraphNodeKind.Step, "Build");
        var stepOrphan = sink.Node("b/tasks.task/t1#step/1", GraphNodeKind.Step, "Deploy cluster");
        var contractOk = sink.Node("contract:builder.build", GraphNodeKind.Contract, "builder.build");
        var contractMissing = sink.Node("contract:cluster.rollout", GraphNodeKind.Contract, "cluster.rollout");
        var app = sink.Node("b/apps/creator", GraphNodeKind.App, "Creator");

        sink.Edge(task, stepOk, GraphEdgeKind.Owns);
        sink.Edge(task, stepOrphan, GraphEdgeKind.Owns);
        sink.Edge(stepOk, stepOrphan, GraphEdgeKind.Blocks);
        sink.Edge(stepOk, contractOk, GraphEdgeKind.Executes);
        sink.Edge(app, contractOk, GraphEdgeKind.Implements);
        sink.Edge(stepOrphan, contractMissing, GraphEdgeKind.Executes);

        var runHealthy = sink.Node("b/agent.run/r1", GraphNodeKind.Run, "r1");
        var runStuck = sink.Node("b/agent.run/r2", GraphNodeKind.Run, "r2");
        sink.Edge(runHealthy, task, GraphEdgeKind.WorksOn, 1f);
        sink.Edge(runStuck, task, GraphEdgeKind.WorksOn, Rules.StuckRunMinutes);

        var g = Csr.Build(1, sink, [], new int[sink.Nodes.Count].Select(_ => -1).ToArray());
        var findings = Rules.Run(g, PageRank.Compute(g));

        Assert.Contains(findings, f => f.Rule == "orphan-task" && f.Nodes.Single() == g.Ids[stepOrphan]);
        Assert.DoesNotContain(findings, f => f.Rule == "orphan-task" && f.Nodes.Contains(g.Ids[stepOk]));
        Assert.Contains(findings, f => f.Rule == "stuck-run" && f.Nodes.Single() == g.Ids[runStuck]);
        Assert.DoesNotContain(findings, f => f.Rule == "stuck-run" && f.Nodes.Contains(g.Ids[runHealthy]));
    }
}
