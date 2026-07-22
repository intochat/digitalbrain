using System.Text.Json;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;

namespace DigitalBrain.Google;

internal sealed class GmailMcpTransport(HttpClient httpClient) : IGmailMcpTransport, IDisposable
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
                Name = "DigitalBrain Gmail",
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
            throw new InvalidOperationException($"Gmail MCP tool '{tool}' failed.");
        }

        return result.StructuredContent?.Clone()
            ?? throw new InvalidOperationException(
                $"Gmail MCP tool '{tool}' returned no structured content.");
    }

    public void Dispose() => httpClient.Dispose();
}
