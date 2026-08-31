using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Integrations.Mcp;

namespace DigitalBrain.Integrations.Gmail;

public sealed class McpGmailTransport : IGmailTransport
{
    private readonly IMcpIntegrationClient _client;
    private readonly McpIntegrationEndpoint _endpoint;
    private readonly GmailPendingActions? _actions;

    public McpGmailTransport(IMcpIntegrationClient client, McpIntegrationEndpoint endpoint)
    { _client = client; _endpoint = endpoint; }

    internal McpGmailTransport(IMcpIntegrationClient client, McpIntegrationEndpoint endpoint, GmailPendingActions actions)
        : this(client, endpoint) => _actions = actions;

    public Task<string> SearchJsonAsync(string account, string topic, CancellationToken cancellationToken)
        => throw new GmailOperationException("Gmail search requires an explicit authenticated owner.");

    public async Task<string> SearchJsonAsync(
        OwnerId owner,
        string account,
        string topic,
        CancellationToken cancellationToken)
    {
        var query = string.Join(
            ' ',
            new[] { string.IsNullOrWhiteSpace(account) ? "" : $"from:{account}", topic }.Where(static value => !string.IsNullOrWhiteSpace(value)));
        try
        {
            var result = await _client.CallAsync(
                owner, _endpoint, "search_threads",
                new Dictionary<string, object?> { ["query"] = query, ["pageSize"] = 3, ["pageToken"] = "", ["includeTrash"] = false, ["view"] = "THREAD_VIEW_MINIMAL" },
                cancellationToken).ConfigureAwait(false);
            return result.GetRawText();
        }
        catch (GmailAuthenticationRequiredException)
        {
            _actions?.RequireLogin(compose: false, cancellationToken);
            throw;
        }
    }
}
