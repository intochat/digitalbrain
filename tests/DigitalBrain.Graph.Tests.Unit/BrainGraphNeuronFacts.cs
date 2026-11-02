using DigitalBrain.Spi;
using DigitalBrain.Testing;

namespace DigitalBrain.Graph.Tests.Unit;

public sealed record Ticked(int Count) : Signal;

public sealed record TickerState(int Count)
{
    public TickerState() : this(0) { }
}

public interface ITicker
{
    Task Tick();
}

[Neuron("probe.ticker")]
public sealed class TickerNeuron : Neuron<TickerState>, ITicker
{
    public Task Tick() => Save(State with { Count = State.Count + 1 }, new Ticked(State.Count + 1));
}

public interface IFollower
{
    Task Follow(string source);
}

public sealed record FollowerSeen(int Count) : Signal;

[Neuron("probe.follower")]
public sealed class FollowerNeuron : Neuron, IFollower
{
    public Task Follow(string source) => Context.Synapses.Ensure<Ticked>(NeuronId.Parse(source));

    private Task Save(Signal signal) => Publish(signal);

    [On]
    public Task On(Ticked signal) => Save(new FollowerSeen(signal.Count));
}

/// <summary>Lets neurons resolve the TestOS as the brain observer, as a silo registers its kernel.</summary>
internal sealed class ObserverServices : IServiceProvider
{
    public IBrainObserver? Observer { get; set; }

    public object? GetService(Type serviceType) => serviceType == typeof(IBrainObserver) ? Observer : null;
}

public sealed class BrainGraphNeuronFacts
{
    private static readonly BrainId Ada = new("ada");
    private static readonly NeuronId GraphId = new(Ada, "brain.graph", "default");

    private static TestOS Start()
    {
        var services = new ObserverServices();
        var os = TestOS.Local(o => o.Services = services).Register<TickerNeuron>().Register<FollowerNeuron>().Register<BrainGraphNeuron>();
        services.Observer = os;
        return os;
    }

    [Fact]
    public async Task Snapshot_has_the_brain_its_neurons_and_the_synapse_with_its_signal()
    {
        await using var os = Start();
        var ticker = new NeuronId(Ada, "probe.ticker", "t");
        var follower = new NeuronId(Ada, "probe.follower", "f");
        await os.Get<ITicker>(ticker).Tick();
        await os.Get<IFollower>(follower).Follow(ticker.ToString());
        await os.Idle();

        var g = await os.Get<IBrainGraph>(GraphId).Snapshot(GraphLevel.Neurons, null);

        Assert.Equal(1, g.Revision);
        Assert.Contains(Ada.Value, g.Ids);
        Assert.Contains(ticker.ToString(), g.Ids);
        Assert.Contains(follower.ToString(), g.Ids);
        Assert.DoesNotContain(GraphId.ToString(), g.Ids);
        Assert.Contains(g.Ids, id => id.StartsWith("signal:", StringComparison.Ordinal));
        var f = g.IndexOf(follower.ToString());
        var t = g.IndexOf(ticker.ToString());
        Assert.Contains(Enumerable.Range(g.RowStart[f], g.RowStart[f + 1] - g.RowStart[f]), e => g.Col[e] == t && g.EdgeKinds[e] == GraphEdgeKind.Subscribes);
        Assert.Contains(Enumerable.Range(g.RowStart[t], g.RowStart[t + 1] - g.RowStart[t]), e => g.EdgeKinds[e] == GraphEdgeKind.Publishes);
    }

    [Fact]
    public async Task Rebuild_bumps_the_revision_only_when_the_graph_changed()
    {
        await using var os = Start();
        var graph = os.Get<IBrainGraph>(GraphId);
        await os.Get<ITicker>(new NeuronId(Ada, "probe.ticker", "one")).Tick();
        await os.Idle();

        Assert.Equal(1, await graph.Rebuild());
        Assert.Equal(1, await graph.Rebuild());

        await os.Get<ITicker>(new NeuronId(Ada, "probe.ticker", "two")).Tick();
        await os.Idle();
        Assert.Equal(2, await graph.Rebuild());
        var changes = os.Journal(Ada).Signals(GraphId).Count(entry => entry.Signal is GraphChanged);
        Assert.Equal(2, changes);
    }

    [Fact]
    public async Task Levels_group_by_module_prefix_and_focus_on_a_node()
    {
        await using var os = Start();
        var ticker = new NeuronId(Ada, "probe.ticker", "t");
        var follower = new NeuronId(Ada, "probe.follower", "f");
        await os.Get<ITicker>(ticker).Tick();
        await os.Get<IFollower>(follower).Follow(ticker.ToString());
        await os.Idle();
        var graph = os.Get<IBrainGraph>(GraphId);

        var brain = await graph.Snapshot(GraphLevel.Brain, null);
        Assert.Equal(["group:probe"], brain.Ids);

        var cluster = await graph.Snapshot(GraphLevel.Cluster, "probe");
        Assert.Equal(2, cluster.NodeCount);
        Assert.Contains(ticker.ToString(), cluster.Ids);

        var neurons = await graph.Snapshot(GraphLevel.Neurons, ticker.ToString());
        Assert.Contains(follower.ToString(), neurons.Ids);
        Assert.Contains(ticker.ToString(), neurons.Ids);

        Assert.Equal([follower.ToString(), ticker.ToString()], (await graph.Paths(follower.ToString(), ticker.ToString(), 3)).Single().Nodes);
        Assert.Contains(ticker.ToString(), await graph.Neighbourhood(follower.ToString(), 1, null));
    }

    [Fact]
    public async Task Another_brain_is_not_in_the_snapshot()
    {
        await using var os = Start();
        await os.Get<ITicker>(new NeuronId(Ada, "probe.ticker", "mine")).Tick();
        await os.Get<ITicker>(new NeuronId(new BrainId("bob"), "probe.ticker", "his")).Tick();
        await os.Idle();

        var g = await os.Get<IBrainGraph>(GraphId).Snapshot(GraphLevel.Neurons, null);

        Assert.DoesNotContain(g.Ids, id => id.StartsWith("bob", StringComparison.Ordinal));
    }
}
