using System.Collections.Concurrent;
using System.Text.Json;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Integrations.Gmail;
using DigitalBrain.Integrations.Salesforce;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DigitalBrain.Integrations.Mcp;

public sealed class McpIntegrationClient : IMcpIntegrationClient, IAsyncDisposable
{
    private readonly SalesforceConnections? _connections;
    private readonly GmailMcpSessions? _gmail;
    private readonly ConcurrentDictionary<OwnerId, SessionSlot> _sessions = new();

    public McpIntegrationClient() { }

    internal McpIntegrationClient(SalesforceConnections? connections) => _connections = connections;
    internal McpIntegrationClient(SalesforceConnections? connections, GmailMcpSessions? gmail)
    { _connections = connections; _gmail = gmail; }

    public Task<JsonElement> CallAsync(OwnerId owner, McpIntegrationEndpoint endpoint, string toolName,
        IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
        => string.Equals(endpoint.Name, "gmail", StringComparison.OrdinalIgnoreCase)
            ? (_gmail ?? throw new GmailOperationException("Gmail is not configured.")).CallAsync(owner, endpoint, toolName, arguments, cancellationToken)
            : CallAsync(endpoint, toolName, arguments, cancellationToken);

    public async Task<JsonElement> CallAsync(
        McpIntegrationEndpoint endpoint,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(arguments);
        if (string.Equals(endpoint.Name, "gmail", StringComparison.OrdinalIgnoreCase))
        {
            throw new GmailOperationException("Gmail requires an explicit authenticated owner.");
        }

        var isSalesforce = string.Equals(endpoint.Name, "salesforce", StringComparison.OrdinalIgnoreCase);
        if (isSalesforce && _connections is not null)
        {
            return await CallSalesforceAsync(endpoint, toolName, arguments, cancellationToken).ConfigureAwait(false);
        }
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

        return await InvokeAsync(client, endpoint, toolName, arguments, cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonElement> CallSalesforceAsync(McpIntegrationEndpoint endpoint, string toolName,
        IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
    {
        var owner = _connections!.CurrentOwner;
        // Known-disconnected is control flow: do not issue unauthenticated HTTP or exception traces.
        _ = await _connections.GetAccessTokenAsync(owner, cancellationToken).ConfigureAwait(false);
        var slot = _sessions.GetOrAdd(owner, static _ => new SessionSlot());
        await slot.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    if (slot.Client is null)
                    {
                        slot.Http = new HttpClient(new SalesforceBearerHandler(_connections, owner));
                        var transport = new HttpClientTransport(new HttpClientTransportOptions
                        {
                            Endpoint = endpoint.Uri,
                            TransportMode = HttpTransportMode.StreamableHttp,
                        }, slot.Http);
                        slot.Client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken).ConfigureAwait(false);
                    }
                    return await InvokeAsync(slot.Client, endpoint, toolName, arguments, cancellationToken).ConfigureAwait(false);
                }
                catch (HttpRequestException error) when (error.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    await slot.ResetAsync().ConfigureAwait(false);
                    if (attempt != 0 || toolName is not ("getUserInfo" or "soqlQuery"))
                    {
                        throw new SalesforceAuthenticationRequiredException();
                    }
                    // One silent refresh/retry for reads. Never automatically replay a mutation.
                    _ = await _connections.GetAccessTokenAsync(owner, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    await slot.ResetAsync().ConfigureAwait(false);
                    throw;
                }
            }
        }
        finally
        {
            slot.Gate.Release();
        }
    }

    private static async Task<JsonElement> InvokeAsync(McpClient client, McpIntegrationEndpoint endpoint,
        string toolName, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
    {
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

    public async ValueTask DisposeAsync()
    {
        foreach (var slot in _sessions.Values)
        {
            await slot.ResetAsync().ConfigureAwait(false);
            slot.Gate.Dispose();
        }
        _sessions.Clear();
    }

    private sealed class SessionSlot
    {
        internal readonly SemaphoreSlim Gate = new(1, 1);
        internal McpClient? Client;
        internal HttpClient? Http;

        internal async ValueTask ResetAsync()
        {
            if (Client is not null)
            {
                try { await Client.DisposeAsync().ConfigureAwait(false); }
                catch (HttpRequestException) { /* Session may already be unauthorized. */ }
                finally { Client = null; }
            }
            Http?.Dispose();
            Http = null;
        }
    }
}
