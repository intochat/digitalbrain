using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Graph;
using DigitalBrain.Testing.Module;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.Unit.Graph;

public sealed class GraphFacts
{
    [Fact]
    public async Task RenderWritesTitleNodesAndEdges()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await ModuleTest.Create().WithModule<FlutterModule>()
            .StartAsync(ct);
        await brain.Get<IGraph>("net").Render("N", [new GraphNode("a", "A")], [new GraphEdge("a", "a")]);
        var state = await brain.Get<IGraph>("net").Read();
        Assert.Equal("N", state.Title);
        Assert.Equal(new GraphNode("a", "A"), Assert.Single(state.Nodes));
        Assert.Equal(new GraphEdge("a", "a"), Assert.Single(state.Edges));
    }
}
