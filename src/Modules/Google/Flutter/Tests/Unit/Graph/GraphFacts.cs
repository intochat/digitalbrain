using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Graph;
using DigitalBrain.Flutter.Graph.Signals;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.Unit.Graph;

public sealed class GraphFacts
{
    [Fact]
    public async Task GraphNeuronPreservesIconAndPublishesTransientActivity()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>()
            .StartAsync(ct);
        var graph = brain.Get<IGraph>("live");
        await graph.Render("Live", [new GraphNode("source", "Source", "supabase")], []);
        await using var pulses = await brain.Observe<GraphActivityObserved>(graph, ct);
        await graph.Emit(new GraphActivityPulse("e1", "source", null, "SignalPublished", "published", 1));
        var pulse = await pulses.NextAsync(ct: ct);
        Assert.Equal("e1", pulse.EventId);
        Assert.Equal("source", pulse.SourceId);
        Assert.Null(pulse.TargetId);
        var state = await graph.Read();
        Assert.Equal("supabase", Assert.Single(state.Nodes).IconKey);
        Assert.Equal(1, state.Version);
    }

    [Fact]
    public async Task PublicationCannotInventDeliveryRoute()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>()
            .StartAsync(ct);
        var graph = brain.Get<IGraph>("live");
        await graph.Render("Live", [new GraphNode("source", "Source"), new GraphNode("target", "Target")], []);
        await Assert.ThrowsAsync<ArgumentException>(() => graph.Emit(
            new GraphActivityPulse("e1", "source", "target", "SignalPublished", "published", 1)));
        Assert.Equal(1, (await graph.Read()).Version);
    }
    [Fact]
    public async Task RenderWritesTitleNodesAndEdges()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>()
            .StartAsync(ct);
        await brain.Get<IGraph>("net").Render("N", [new GraphNode("a", "A")], [new GraphEdge("a", "a")]);
        var state = await brain.Get<IGraph>("net").Read();
        Assert.Equal("N", state.Title);
        Assert.Equal(new GraphNode("a", "A"), Assert.Single(state.Nodes));
        Assert.Equal(new GraphEdge("a", "a"), Assert.Single(state.Edges));
    }
}
