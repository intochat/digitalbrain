using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace DigitalBrain.E2E.Tests;

// A real ModelContextProtocol.Core client crosses the Streamable HTTP tool surface.
[Collection(E2ECollection.Name)]
public sealed class McpSurfaceTests(AppHostFixture fixture)
{
    // McpSurface.cs (DigitalBrain.Mcp) and ProductSurfaceResources.cs (DigitalBrain.AppHost)
    // declare these as `internal`, so they aren't visible from this test project; duplicated
    // here per this repo's established pattern (see
    // tests/DigitalBrain.Aspire.Tests/ProductSurfaceResourceNames.cs) rather than granting
    // InternalsVisibleTo.
    private const string McpPath = "/mcp";
    private const string ReadNeuronJournalTool = "read_neuron_journal";
    private const string ReadChatTranscriptTool = "read_chat_transcript";

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
        foreach (var expectedTool in new[] { ReadNeuronJournalTool, ReadChatTranscriptTool })
        {
            Assert.Contains(expectedTool, toolNames);
        }
    }
}
