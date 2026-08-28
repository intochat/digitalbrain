using DigitalBrain.Integrations.Mcp;

namespace DigitalBrain.Integrations.Gmail;

public sealed class McpGmailTransport(
    IMcpIntegrationClient client,
    McpIntegrationEndpoint endpoint) : IGmailTransport
{
    public async Task<string> SearchJsonAsync(
        string account,
        string topic,
        CancellationToken cancellationToken)
    {
        var query = string.Join(
            ' ',
            new[] { $"from:{account}", topic }.Where(static value => !string.IsNullOrWhiteSpace(value)));
        var result = await client.CallAsync(
            endpoint,
            "search_threads",
            new Dictionary<string, object?> { ["query"] = query },
            cancellationToken).ConfigureAwait(false);
        return result.GetRawText();
    }
}
