using System.Text.Json;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;

namespace DigitalBrain.Salesforce;

internal sealed class SalesforceMcpTransport(HttpClient httpClient) : ISalesforceMcpTransport, IDisposable
{
    public async ValueTask<JsonElement> CallToolAsync(
        Uri endpoint,
        ClientOAuthOptions authorization,
        string tool,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentException.ThrowIfNullOrWhiteSpace(tool);
        ArgumentNullException.ThrowIfNull(arguments);

        await using var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = endpoint,
                Name = "DigitalBrain Salesforce",
                OAuth = authorization,
            },
            httpClient);
        await using var client = await McpClient.CreateAsync(
            transport,
            cancellationToken: cancellationToken);
        var result = await client.CallToolAsync(
            tool,
            arguments,
            cancellationToken: cancellationToken);

        if (result.IsError is true)
        {
            throw new InvalidOperationException($"Salesforce MCP tool '{tool}' failed.");
        }

        return result.StructuredContent?.Clone()
            ?? throw new InvalidOperationException(
                $"Salesforce MCP tool '{tool}' returned no structured content.");
    }

    public void Dispose() => httpClient.Dispose();
}
