using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

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

        var isSalesforce = string.Equals(endpoint.Name, "salesforce", StringComparison.OrdinalIgnoreCase);
        using var handler = new HttpClientHandler { AllowAutoRedirect = !isSalesforce };
        using var http = new HttpClient(handler);
        // Set before connecting so initialization, discovery, and calls all authenticate.
        endpoint.ConfigureHttpClient(http);
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
        if (result.IsError == true)
        {
            throw new InvalidOperationException(
                $"MCP tool '{endpoint.Name}/{toolName}' reported an error. Check server permissions and request arguments.");
        }

        if (result.StructuredContent is JsonElement structured)
        {
            return structured.Clone();
        }

        // Hosted servers can return JSON in text content instead of structuredContent.
        var text = string.Join('\n', result.Content.OfType<TextContentBlock>().Select(static block => block.Text));
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException($"MCP tool '{endpoint.Name}/{toolName}' returned no content.");
        }
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(text);
        }
    }
}
