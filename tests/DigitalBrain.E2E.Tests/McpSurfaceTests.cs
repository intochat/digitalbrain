using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace DigitalBrain.E2E.Tests;

// The C3 parked ruling: the MCP brain_connect tool surface had never been crossed by any test.
// This is the first real ModelContextProtocol.Core client against the frozen tool surface --
// a genuine Streamable HTTP session against the running mcp resource, not an in-process call
// into BrainTools.
[Collection(E2ECollection.Name)]
public sealed class McpSurfaceTests(AppHostFixture fixture)
{
    // McpSurface.cs (DigitalBrain.Mcp) and ProductSurfaceResources.cs (DigitalBrain.AppHost)
    // declare these as `internal`, so they aren't visible from this test project; duplicated
    // here per this repo's established pattern (see
    // tests/DigitalBrain.Aspire.Tests/ProductSurfaceResourceNames.cs) rather than granting
    // InternalsVisibleTo.
    private const string McpPath = "/mcp";
    private const string ListActiveNeuronsTool = "list_active_neurons";
    private const string BrainConnectTool = "brain_connect";
    private const string BrainDisconnectTool = "brain_disconnect";
    private const string ReadNeuronJournalTool = "read_neuron_journal";
    private const string ReadChartTool = "read_chart";

    [Fact]
    public async Task TheFrozenMcpToolsAnswerOverTheRealProtocol()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // The mcp resource's HTTP endpoint is named "mcp" (ProductSurfaceResources.McpHttpEndpointName),
        // not the "http" default CreateHttpClient assumes when no endpoint name is given.
        using var http = fixture.CreateHttpClient("mcp", "mcp");

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(http.BaseAddress!, McpPath),
                TransportMode = HttpTransportMode.StreamableHttp,
            },
            http);

        await using var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);

        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
        var toolNames = tools.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var expectedTool in new[]
                 {
                     ListActiveNeuronsTool, BrainConnectTool, BrainDisconnectTool, ReadNeuronJournalTool, ReadChartTool,
                 })
        {
            Assert.Contains(expectedTool, toolNames);
        }

        var wireArguments = new Dictionary<string, object?>
        {
            ["source"] = "uirenderer:wire-a",
            ["synapseAlias"] = "demo.wire",
            ["target"] = "uirenderer:wire-b",
        };

        var connect = await client.CallToolAsync(BrainConnectTool, wireArguments, cancellationToken: cancellationToken);
        Assert.False(connect.IsError is true);
        Assert.Contains("Connected", TextOf(connect), StringComparison.Ordinal);

        try
        {
            var active = await client.CallToolAsync(ListActiveNeuronsTool, cancellationToken: cancellationToken);
            Assert.False(active.IsError is true);
            Assert.NotEmpty(active.Content);
        }
        finally
        {
            var disconnect = await client.CallToolAsync(BrainDisconnectTool, wireArguments, cancellationToken: cancellationToken);
            Assert.False(disconnect.IsError is true);
            Assert.Contains("Disconnected", TextOf(disconnect), StringComparison.Ordinal);
        }
    }

    private static string TextOf(CallToolResult result)
        => string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));
}
