using DigitalBrain.Abstractions;
using DigitalBrain.Tests.Harness;
using DigitalBrain.UI;
using Xunit;

namespace DigitalBrain.Tests;

[Collection(BrainCollection.Name)]
public sealed class DiagramProofs(BrainClusterFixture fixture)
{
    [Fact]
    public async Task NodesAndEdgesUpsertByIdentity()
    {
        var brain = fixture.BrainFor("diagram");
        var token = TestContext.Current.CancellationToken;

        await brain.SendAsync<IDiagram>("main", new Node("a", "Alpha"), token);
        await brain.SendAsync<IDiagram>("main", new Node("b", "Beta"), token);
        await brain.SendAsync<IDiagram>("main", new Edge("a-b", "a", "b"), token);
        await brain.SendAsync<IDiagram>("main", new Node("a", "Alpha v2"), token);

        var document = await WaitForDocumentAsync(
            brain,
            read => read.Nodes.Count == 2
                && read.Edges.Count == 1
                && read.Nodes.Any(static node => node is { NodeId: "a", Label: "Alpha v2" }));
        Assert.Contains(document.Nodes, static node => node is { NodeId: "b", Label: "Beta" });
        Assert.Equal("a", document.Edges.Single().SourceNodeId);
    }

    [Fact]
    public async Task RoutedFactBecomesADiagramNode()
    {
        var brain = fixture.BrainFor("diagram-route");
        var feed = NeuronId.For<IProbeSource>(brain.Owner, "elon");
        var diagram = NeuronId.For<IDiagram>(brain.Owner, "main");

        await brain.SendAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Connect(
                Guid.NewGuid(),
                feed,
                "probe.fact",
                diagram,
                "to:ui.node{NodeId=Text,Label=Text}"),
            TestContext.Current.CancellationToken);
        await Graphs.WaitForConnectionsAsync(brain, feed, "probe.fact");

        await brain.SendAsync<IProbeSource>(
            "elon", new Poke("mars"), TestContext.Current.CancellationToken);

        await WaitForDocumentAsync(
            brain,
            read => read.Nodes.Any(static node => node is { NodeId: "mars", Label: "mars" }));
    }

    private static async Task<DiagramRead> WaitForDocumentAsync(
        Client.IDigitalBrain brain,
        Func<DiagramRead, bool> expectation)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (true)
        {
            var document = await brain.GetGrainProxy<IDiagram>("main").Read();
            if (expectation(document))
            {
                return document;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The diagram never reached the expected document.");
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
    }
}
