using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Graph;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class GraphFacts
{
    [Fact]
    public async Task RenderWritesNodes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.StartAsync(new() { Modules = [new FlutterModule()] }, ct);
        await brain.Get<IGraph>("net").Render("N", [new GraphNode("a", "A")], [new GraphEdge("a", "a")]);
        Assert.Single((await brain.Get<IGraph>("net").Read()).Nodes);
    }
}
