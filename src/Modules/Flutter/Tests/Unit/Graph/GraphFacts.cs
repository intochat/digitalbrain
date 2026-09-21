using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Graph;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.Unit.Graph;

public sealed class GraphFacts
{
    [Fact]
    public async Task RenderWritesNodes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>()
            .StartAsync(ct);
        await brain.Get<IGraph>("net").Render("N", [new GraphNode("a", "A")], [new GraphEdge("a", "a")]);
        Assert.Single((await brain.Get<IGraph>("net").Read()).Nodes);
    }
}
