using DigitalBrain.Testing;

// What the DigitalBrain source generator writes for a build: the follower calls the ticker's contract and the ticker calls the follower's.
[assembly: DigitalBrain.Calls("probe.follower", "probe.ticker")]
[assembly: DigitalBrain.Calls("probe.ticker", "probe.follower")]

namespace DigitalBrain.Graph.Tests.Unit;

public sealed class CallsFacts
{
    private static readonly BrainId Ada = new("ada");

    // Two neuron types whose contracts call each other: with one instance each, and contracts implemented by those instances, the
    // calls edges close a cycle through the contract nodes. The rule reports it so a non-reentrant A→B→A is caught before it deadlocks.
    [Fact]
    public async Task Calls_edges_come_from_assembly_attributes_and_a_cycle_is_a_finding()
    {
        var services = new ObserverServices();
        await using var os = TestOS.Local(o => o.Services = services).Register<TickerNeuron>().Register<FollowerNeuron>().Register<BrainGraphNeuron>();
        services.Observer = os;
        var ticker = new NeuronId(Ada, "probe.ticker", "t");
        var follower = new NeuronId(Ada, "probe.follower", "f");
        await os.Get<ITicker>(ticker).Tick();
        await os.Get<IFollower>(follower).Follow(ticker.ToString());
        await os.Idle();
        var graph = os.Get<IBrainGraph>(new NeuronId(Ada, "brain.graph", "default"));

        var g = await graph.Snapshot(GraphLevel.Neurons, null);
        var f = g.IndexOf(follower.ToString());
        var contract = g.IndexOf("contract:probe.ticker");
        Assert.True(contract >= 0);
        Assert.Contains(Enumerable.Range(g.RowStart[f], g.RowStart[f + 1] - g.RowStart[f]), e => g.Col[e] == contract && g.EdgeKinds[e] == GraphEdgeKind.Calls);

        var analysis = await graph.Analysis();
        var cycle = Assert.Single(analysis.Findings, finding => finding.Rule == "call-cycle");
        Assert.Equal([follower.ToString(), ticker.ToString()], cycle.Nodes.Order());
    }
}

public sealed class SessionAttributionFacts
{
    [Fact]
    public void ARuntimeSessionSubscriptionBelongsToTheProgramItServes()
    {
        var sink = new GraphSink();
        var app = sink.Node("ada/apps/desktop", GraphNodeKind.App, "apps/desktop");

        Assert.Equal(app, GraphBuilder.Session(sink, "ada/runtime.session/csharp/ada/apps/desktop"));
        Assert.Equal(-1, GraphBuilder.Session(sink, "ada/runtime.session/csharp/ada/apps/other"));
        Assert.Equal(-1, GraphBuilder.Session(sink, "ada/runtime.session/client"));
        Assert.Equal(-1, GraphBuilder.Session(sink, "ada/probe.ticker/t"));
    }
}
