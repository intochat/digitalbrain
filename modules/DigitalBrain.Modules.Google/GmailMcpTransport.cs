using System.Text.Json;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;

namespace DigitalBrain.Google;

internal sealed class GmailMcpTransport(IHttpClientFactory httpClients) : IGmailMcpTransport
{
    internal const string HttpClientName = "DigitalBrain.Google.Mcp";

    public async ValueTask<McpToolSnapshot> ReadToolAsync(
        Uri endpoint,
        ClientOAuthOptions authorization,
        string tool,
        CancellationToken cancellationToken)
    {
        await using var session = await OpenAsync(endpoint, authorization, cancellationToken);
        var tools = await session.Client.ListToolsAsync(cancellationToken: cancellationToken);
        var advertised = tools.SingleOrDefault(
            candidate => string.Equals(candidate.Name, tool, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Gmail MCP did not advertise required tool '{tool}'.");

        var snapshot = Snapshot(advertised);
        Admit(snapshot);
        return snapshot;
    }

    public async ValueTask<JsonElement> CallToolAsync(
        Uri endpoint,
        ClientOAuthOptions authorization,
        string tool,
        IReadOnlyDictionary<string, object?> arguments,
        string expectedSchemaFingerprint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentException.ThrowIfNullOrWhiteSpace(tool);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSchemaFingerprint);

        await using var session = await OpenAsync(endpoint, authorization, cancellationToken);
        var tools = await session.Client.ListToolsAsync(cancellationToken: cancellationToken);
        var advertised = tools.SingleOrDefault(
            candidate => string.Equals(candidate.Name, tool, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Gmail MCP did not advertise required tool '{tool}'.");

        var snapshot = Snapshot(advertised);

        if (!string.Equals(
            snapshot.SchemaFingerprint,
            expectedSchemaFingerprint,
            StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Gmail MCP tool '{tool}' schema changed after admission.");
        }

        Admit(snapshot);

        var result = await session.Client.CallToolAsync(
            tool,
            arguments,
            cancellationToken: cancellationToken);

        if (result.IsError is true)
        {
            throw new InvalidOperationException($"Gmail MCP tool '{tool}' failed.");
        }

        return result.StructuredContent?.Clone()
            ?? throw new InvalidOperationException(
                $"Gmail MCP tool '{tool}' returned no structured content.");
    }

    private static McpToolSnapshot Snapshot(McpClientTool tool) => McpToolSnapshot.Create(
        tool.Name,
        tool.ProtocolTool.InputSchema,
        tool.ProtocolTool.Annotations?.ReadOnlyHint,
        tool.ProtocolTool.Annotations?.DestructiveHint);

    private static void Admit(McpToolSnapshot tool)
    {
        if (!string.Equals(tool.Name, "get_message", StringComparison.Ordinal)
            || tool.ReadOnly is not true
            || tool.Destructive is true
            || !HasRequiredProperty(tool.InputSchema, "messageId", "string"))
        {
            throw new InvalidOperationException(
                $"Gmail MCP tool '{tool.Name}' is incompatible with the admitted read-only contract.");
        }
    }

    private static bool HasRequiredProperty(JsonElement schema, string name, string type)
    {
        if (!schema.TryGetProperty("type", out var schemaType)
            || !string.Equals(schemaType.GetString(), "object", StringComparison.Ordinal)
            || !schema.TryGetProperty("properties", out var properties)
            || !properties.TryGetProperty(name, out var property)
            || !property.TryGetProperty("type", out var propertyType)
            || !string.Equals(propertyType.GetString(), type, StringComparison.Ordinal)
            || !schema.TryGetProperty("required", out var required)
            || required.ValueKind is not JsonValueKind.Array)
        {
            return false;
        }

        return required.EnumerateArray().Any(candidate =>
            string.Equals(candidate.GetString(), name, StringComparison.Ordinal));
    }

    private async ValueTask<McpSession> OpenAsync(
        Uri endpoint,
        ClientOAuthOptions authorization,
        CancellationToken cancellationToken)
    {
        var httpClient = httpClients.CreateClient(HttpClientName);
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = endpoint,
                Name = "DigitalBrain Gmail",
                OAuth = authorization,
            },
            httpClient);

        try
        {
            var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
            return new McpSession(httpClient, transport, client);
        }
        catch
        {
            await transport.DisposeAsync();
            httpClient.Dispose();
            throw;
        }
    }

    private sealed class McpSession(
        HttpClient httpClient,
        HttpClientTransport transport,
        McpClient client) : IAsyncDisposable
    {
        internal McpClient Client { get; } = client;

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            await transport.DisposeAsync();
            httpClient.Dispose();
        }
    }
}
