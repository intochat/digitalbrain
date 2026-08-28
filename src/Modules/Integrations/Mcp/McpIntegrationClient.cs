using System.Text.Json;
using ModelContextProtocol.Client;

namespace DigitalBrain.Integrations.Mcp;

public sealed class McpIntegrationClient : IMcpIntegrationClient
{
    public async Task<JsonElement> CallAsync(
        McpIntegrationEndpoint endpoint,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(arguments);

        using var http = new HttpClient();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = endpoint.Uri,
                TransportMode = HttpTransportMode.StreamableHttp,
            },
            http);
        await using var client = await McpClient.CreateAsync(
            transport,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!tools.Any(tool => string.Equals(tool.Name, toolName, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"MCP server '{endpoint.Name}' does not publish tool '{toolName}'.");
        }

        var result = await client.CallToolAsync(
            toolName,
            arguments,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.StructuredContent is not JsonElement structured)
        {
            throw new InvalidOperationException(
                $"MCP tool '{endpoint.Name}/{toolName}' returned no structured content.");
        }

        return structured.Clone();
    }
}
