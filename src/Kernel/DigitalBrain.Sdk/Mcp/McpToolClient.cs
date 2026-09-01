using System.Net;
using System.Text.Json;
using DigitalBrain.Abstractions.Identity;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DigitalBrain.Sdk;

// One hosted MCP server, one cached authenticated session per owner. The provider supplies
// credentials and policy; this class owns the transport, the session lifecycle, tool lookup,
// result normalization and the single read-only retry after a rejected credential.
public sealed class McpToolClient<TConnection> : IAsyncDisposable
    where TConnection : notnull
{
    private readonly IMcpCredentials<TConnection> _credentials;
    private readonly McpToolPolicy _policy;
    private readonly McpSessionOptions _options;
    private readonly Dictionary<OwnerId, Session> _sessions = [];

    public McpToolClient(
        McpEndpoint endpoint,
        IMcpCredentials<TConnection> credentials,
        McpToolPolicy policy,
        McpSessionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(policy);
        Endpoint = endpoint;
        _credentials = credentials;
        _policy = policy;
        _options = options ?? new McpSessionOptions();
    }

    public McpEndpoint Endpoint { get; }

    public async Task<JsonElement> CallAsync(
        OwnerId owner,
        string tool,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tool);
        ArgumentNullException.ThrowIfNull(arguments);
        var connection = _credentials.Connection(owner);
        var session = Reserve(owner);
        await session.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var succeeded = false;
        try
        {
            if (!EqualityComparer<TConnection>.Default.Equals(session.Connection, connection) || session.Expired)
            {
                await session.ResetAsync().ConfigureAwait(false);
            }

            var budget = _options.ResponseBudgetBytes is { } limit ? new BearerTokenHandler.ResponseBudget(limit) : null;
            session.Handler?.BeginOperation(budget);
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    _ = await _credentials.AccessTokenAsync(owner, connection, refresh: false, cancellationToken).ConfigureAwait(false);
                    if (session.Client is null)
                    {
                        await ConnectAsync(session, owner, connection, budget, cancellationToken).ConfigureAwait(false);
                    }

                    var result = await InvokeAsync(session, tool, arguments, cancellationToken).ConfigureAwait(false);
                    succeeded = true;
                    return result;
                }
                catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.Unauthorized)
                {
                    await session.ResetAsync().ConfigureAwait(false);
                    if (attempt != 0 || !_policy.IsReadOnly(tool))
                    {
                        await _credentials.RejectAsync(owner, connection, cancellationToken).ConfigureAwait(false);
                        throw new McpAuthenticationRequiredException();
                    }

                    _ = await _credentials.AccessTokenAsync(owner, connection, refresh: true, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            if (!succeeded)
            {
                await session.ResetAsync().ConfigureAwait(false);
            }

            session.Gate.Release();
        }
    }

    public async Task PruneAsync()
    {
        Session[] sessions;
        lock (_sessions)
        {
            sessions = [.. _sessions.Values];
        }

        foreach (var session in sessions)
        {
            if (!session.Gate.Wait(0))
            {
                continue;
            }

            try
            {
                if (session.Expired)
                {
                    await session.ResetAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                session.Gate.Release();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        Session[] sessions;
        lock (_sessions)
        {
            sessions = [.. _sessions.Values];
            _sessions.Clear();
        }

        foreach (var session in sessions)
        {
            await session.ResetAsync().ConfigureAwait(false);
            session.Gate.Dispose();
        }
    }

    private Session Reserve(OwnerId owner)
    {
        lock (_sessions)
        {
            if (_sessions.TryGetValue(owner, out var existing))
            {
                return existing;
            }

            if (_sessions.Count >= _options.Capacity)
            {
                throw new McpOperationException(
                    $"MCP server '{Endpoint.Name}' reached its session capacity. Restart the kernel to clear unused connections.");
            }

            var session = new Session();
            _sessions.Add(owner, session);
            return session;
        }
    }

    private async Task ConnectAsync(
        Session session,
        OwnerId owner,
        TConnection connection,
        BearerTokenHandler.ResponseBudget? budget,
        CancellationToken cancellationToken)
    {
        session.Connection = connection;
        session.ExpiresAt = _options.Lifetime is { } lifetime ? DateTimeOffset.UtcNow.Add(lifetime) : DateTimeOffset.MaxValue;
        session.Handler = new BearerTokenHandler(
            Endpoint,
            token => _credentials.AccessTokenAsync(owner, connection, refresh: false, token));
        session.Handler.BeginOperation(budget);
        session.Http = new HttpClient(session.Handler);
        if (_options.Timeout is { } timeout)
        {
            session.Http.Timeout = timeout;
        }

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = Endpoint.Uri,
                TransportMode = HttpTransportMode.StreamableHttp,
                EnableStandaloneGetStream = false,
                MaxReconnectionAttempts = 0,
            },
            session.Http);
        session.Client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken).ConfigureAwait(false);
        var tools = await session.Client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        _policy.ValidateCatalog?.Invoke(tools);
        session.Tools = tools.Select(static tool => tool.Name).ToHashSet(StringComparer.Ordinal);
    }

    private async Task<JsonElement> InvokeAsync(
        Session session,
        string tool,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        if (session.Tools is null || !session.Tools.Contains(tool))
        {
            throw new McpOperationException($"MCP server '{Endpoint.Name}' does not publish tool '{tool}'.");
        }

        var result = await session.Client!.CallToolAsync(tool, arguments, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.IsError == true)
        {
            throw new McpOperationException(
                $"MCP tool '{Endpoint.Name}/{tool}' reported an error. Check server permissions and request arguments.");
        }

        if (result.StructuredContent is JsonElement structured)
        {
            return structured.Clone();
        }

        var text = string.Join('\n', result.Content.OfType<TextContentBlock>().Select(static block => block.Text));
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new McpOperationException($"MCP tool '{Endpoint.Name}/{tool}' returned no content.");
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

    private sealed class Session
    {
        internal readonly SemaphoreSlim Gate = new(1, 1);
        internal TConnection? Connection;
        internal DateTimeOffset ExpiresAt;
        internal McpClient? Client;
        internal HttpClient? Http;
        internal BearerTokenHandler? Handler;
        internal HashSet<string>? Tools;

        internal bool Expired => Client is not null && ExpiresAt <= DateTimeOffset.UtcNow;

        // HTTP goes first: an expiring session must not refresh credentials just to DELETE itself.
        internal async ValueTask ResetAsync()
        {
            Http?.Dispose();
            Http = null;
            Handler = null;
            Tools = null;
            if (Client is not null)
            {
                try
                {
                    await Client.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                }

                Client = null;
            }

            Connection = default;
        }
    }
}
