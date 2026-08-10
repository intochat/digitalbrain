using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Modules.Sdk.Mcp;
using DigitalBrain.Tests.Harness;
using DigitalBrain.UI;
using Xunit;

namespace DigitalBrain.Tests;

[Collection(BrainCollection.Name)]
public sealed class McpGatewayProofs(BrainClusterFixture fixture)
{
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    [Fact]
    public async Task ListingAnswersWithTheServersLiveCatalog()
    {
        var brain = fixture.BrainFor("mcp-list");

        var listed = await brain.Get<IMcp>("crm").FireAsync(
            new ListMcpTools(CommandId.New()),
            TestContext.Current.CancellationToken);

        Assert.Contains(listed.Tools, tool => tool.Name == "soqlQuery" && !tool.Destructive);
        Assert.Contains(listed.Tools, tool => tool.Name == "updateSobjectRecord" && tool.Destructive);
    }

    [Fact]
    public async Task CallingReturnsTheStructuredContent()
    {
        var brain = fixture.BrainFor("mcp-call");

        var returned = await brain.Get<IMcp>("crm").FireAsync(
            new CallMcpTool(
                CommandId.New(),
                "soqlQuery",
                Json("""{"query":"SELECT week Label, amount Value FROM sales"}""")),
            TestContext.Current.CancellationToken);

        Assert.Equal("soqlQuery", returned.Tool);
        Assert.Equal(0, returned.FiredRows);
        Assert.Contains("records", returned.Content.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FiredRowsReachTheChartThroughTheGraph()
    {
        var brain = fixture.BrainFor("mcp-chart");
        var gateway = new NeuronId("mcp", brain.Owner, "crm");
        var chart = NeuronId.For<IChart>(brain.Owner, "dashboard");

        await brain.FireAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Connect(Guid.NewGuid(), gateway, ChartPoint.AliasName, chart),
            TestContext.Current.CancellationToken);
        await Graphs.WaitForConnectionsAsync(brain, gateway, ChartPoint.AliasName);

        var returned = await brain.Get<IMcp>("crm").FireAsync(
            new CallMcpTool(
                CommandId.New(),
                "soqlQuery",
                Json("""{"query":"SELECT week Label, amount Value FROM sales"}"""),
                FireRowsAs: ChartPoint.AliasName),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, returned.FiredRows);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (true)
        {
            var points = await brain.GetGrainProxy<IChart>("dashboard").Read();
            if (points.Any(static point => point is { Label: "W1", Value: 100 })
                && points.Any(static point => point is { Label: "W2", Value: 250 }))
            {
                return;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The chart never received the fired MCP rows.");
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task DestructiveToolsRefuseWithoutTheApprovalFlow()
    {
        var brain = fixture.BrainFor("mcp-destructive");
        var gateway = new NeuronId("mcp", brain.Owner, "crm");

        await brain.FireAsync(
            gateway,
            new CallMcpTool(CommandId.New(), "updateSobjectRecord", Json("{}")),
            TestContext.Current.CancellationToken);
        await Journals.WaitForAsync(
            brain, gateway, JournalKind.Incoming,
            delivery => delivery.Synapse is CallMcpTool { Tool: "updateSobjectRecord" });

        var outgoing = await brain.ReadJournalAsync(
            gateway, JournalKind.Outgoing, cancellationToken: TestContext.Current.CancellationToken);
        Assert.DoesNotContain(
            outgoing.Delta,
            delivery => delivery.Synapse is McpToolReturned { Tool: "updateSobjectRecord" });
    }

    [Fact]
    public async Task AnUnknownServerRefusesNamingTheConfiguredOnes()
    {
        var brain = fixture.BrainFor("mcp-unknown");
        var gateway = new NeuronId("mcp", brain.Owner, "hubspot");

        await brain.FireAsync(
            gateway,
            new ListMcpTools(CommandId.New()),
            TestContext.Current.CancellationToken);
        await Journals.WaitForAsync(
            brain, gateway, JournalKind.Incoming,
            delivery => delivery.Synapse is ListMcpTools);

        var outgoing = await brain.ReadJournalAsync(
            gateway, JournalKind.Outgoing, cancellationToken: TestContext.Current.CancellationToken);
        Assert.DoesNotContain(outgoing.Delta, delivery => delivery.Synapse is McpToolsListed);
    }
}
