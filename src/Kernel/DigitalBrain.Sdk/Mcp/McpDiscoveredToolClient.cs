using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DigitalBrain.Sdk;

/// <summary>
/// Owns one STDIO connection's isolated identity sessions and exposes the server's tools without
/// translating their schemas. Failed calls are never replayed; the next operation reconnects.
/// </summary>
public sealed class McpDiscoveredToolClient<TIdentity> : IAsyncDisposable where TIdentity : notnull
{
    private readonly McpStdioConnection _connection;
    private readonly HashSet<string> _allowed;
    private readonly Func<TIdentity, CancellationToken, Task<McpClient>> _connect;
    private readonly Func<McpClient, TIdentity, CancellationToken, Task>? _configureSession;
    private readonly Dictionary<TIdentity, Session> _sessions = [];
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _maintenance;
    private bool _disposed;

    public McpDiscoveredToolClient(
        McpStdioConnection connection,
        Func<McpClient, TIdentity, CancellationToken, Task>? configureSession = null)
        : this(connection, configureSession, null) { }

    internal McpDiscoveredToolClient(
        McpStdioConnection connection,
        Func<McpClient, TIdentity, CancellationToken, Task>? configureSession,
        Func<TIdentity, CancellationToken, Task<McpClient>>? connect)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(connection.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(connection.Command);
        ArgumentOutOfRangeException.ThrowIfLessThan(connection.Capacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(connection.ResponseBudgetBytes, 512);
        if (connection.OperationTimeout <= TimeSpan.Zero || connection.IdleTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentException("MCP operation and idle timeouts must be positive.", nameof(connection));
        }

        _connection = connection with { Arguments = [.. connection.Arguments] };
        _allowed = new HashSet<string>(connection.AllowedToolNames, StringComparer.Ordinal);
        _configureSession = configureSession;
        _connect = connect ?? ConnectStdioAsync;
        _maintenance = MaintainAsync();
    }

    public Task<IReadOnlyList<AIFunction>> GetToolsAsync(TIdentity identity, CancellationToken cancellationToken = default)
        => WithSessionAsync<IReadOnlyList<AIFunction>>(identity, async (session, token) =>
        {
            await PrepareAsync(session, identity, token).ConfigureAwait(false);
            return session.Tools!.Values.Where(tool => _allowed.Contains(tool.Name))
                .Select(tool => (AIFunction)new McpDiscoveredTool(_connection.Name, tool,
                    (arguments, invocationToken) => new ValueTask<object?>(InvokeAsync(identity, tool.ProtocolTool, arguments, invocationToken))))
                .ToArray();
        }, cancellationToken);

    /// <summary>Disconnect this identity. Its next preparation or invocation obtains a fresh catalog.</summary>
    public async Task InvalidateAsync(TIdentity identity, CancellationToken cancellationToken = default)
    {
        Session? session;
        lock (_sessions)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_sessions.TryGetValue(identity, out session))
            {
                return;
            }
            session.Users++;
        }

        try
        {
            await session.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try { await session.ResetAsync().ConfigureAwait(false); }
            finally { session.Gate.Release(); }
        }
        finally { Release(session); }
    }

    /// <summary>Remove unused identity slots as well as disposing their processes.</summary>
    public async Task PruneAsync(CancellationToken cancellationToken = default)
    {
        List<Session> expired = [];
        lock (_sessions)
        {
            var cutoff = DateTimeOffset.UtcNow - _connection.IdleTimeout;
            foreach (var pair in _sessions.ToArray())
            {
                if (pair.Value.Users == 0 && pair.Value.LastUsed <= cutoff)
                {
                    _sessions.Remove(pair.Key);
                    expired.Add(pair.Value);
                }
            }
        }

        foreach (var session in expired)
        {
            // Once removed there are no callers, and cancellation must not leak its process.
            await session.ResetAsync().ConfigureAwait(false);
            session.Gate.Dispose();
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    public async ValueTask DisposeAsync()
    {
        Session[] sessions;
        lock (_sessions)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            sessions = [.. _sessions.Values];
            _sessions.Clear();
        }

        await _shutdown.CancelAsync().ConfigureAwait(false);
        await _maintenance.ConfigureAwait(false);
        foreach (var session in sessions)
        {
            await session.Gate.WaitAsync().ConfigureAwait(false);
            try { await session.ResetAsync().ConfigureAwait(false); }
            finally { session.Gate.Release(); }
            // Queued operations can still be unwinding cancellation; do not dispose their gate.
        }

        _shutdown.Dispose();
    }

    private async Task<object?> InvokeAsync(TIdentity identity, Tool snapshot, AIFunctionArguments arguments, CancellationToken cancellationToken)
        => await WithSessionAsync<object?>(identity, async (session, token) =>
        {
            await PrepareAsync(session, identity, token).ConfigureAwait(false);
            if (!_allowed.Contains(snapshot.Name) || !session.Tools!.TryGetValue(snapshot.Name, out var tool))
            {
                throw new McpOperationException($"MCP connection '{_connection.Name}' no longer publishes permitted tool '{snapshot.Name}'. Refresh the tool catalog.");
            }

            if (!SameSchema(snapshot.InputSchema, tool.ProtocolTool.InputSchema) ||
                !SameSchema(snapshot.OutputSchema, tool.ProtocolTool.OutputSchema))
            {
                throw new McpOperationException($"MCP tool '{snapshot.Name}' changed its schema. Refresh the tool catalog before calling it.");
            }

            var result = await tool.CallAsync(new Dictionary<string, object?>(arguments), cancellationToken: token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return SerializeBounded(result);
        }, cancellationToken).ConfigureAwait(false);

    private async Task<TResult> WithSessionAsync<TResult>(
        TIdentity identity, Func<Session, CancellationToken, Task<TResult>> action, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        deadline.CancelAfter(_connection.OperationTimeout);
        var session = Reserve(identity);
        try
        {
            await session.Gate.WaitAsync(deadline.Token).ConfigureAwait(false);
            try
            {
                return await action(session, deadline.Token).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                await session.ResetAsync().ConfigureAwait(false);
                if (exception is OperationCanceledException or McpOperationException)
                {
                    throw;
                }
                // Protocol messages and child-process errors may contain paths, arguments or credentials.
                throw new McpOperationException($"MCP connection '{_connection.Name}' failed ({exception.GetType().Name}). The next request will reconnect; this operation was not replayed.");
            }
            finally { session.Gate.Release(); }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !_shutdown.IsCancellationRequested)
        {
            throw new TimeoutException($"MCP connection '{_connection.Name}' exceeded its operation deadline. Cancellation was requested, but a remote outcome is not confirmed. The operation was not replayed.");
        }
        finally { Release(session); }
    }

    private Session Reserve(TIdentity identity)
    {
        lock (_sessions)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_sessions.TryGetValue(identity, out var session))
            {
                if (_sessions.Count >= _connection.Capacity)
                {
                    throw new McpOperationException($"MCP connection '{_connection.Name}' has no free identity sessions. Retry after idle sessions expire.");
                }

                _sessions.Add(identity, session = new Session());
            }

            session.Users++;
            return session;
        }
    }

    private void Release(Session session)
    {
        lock (_sessions)
        {
            session.Users--;
            session.LastUsed = DateTimeOffset.UtcNow;
        }
    }

    private async Task PrepareAsync(Session session, TIdentity identity, CancellationToken cancellationToken)
    {
        if (session.Client is null)
        {
            session.Client = await _connect(identity, cancellationToken).ConfigureAwait(false);
            session.Notification = session.Client.RegisterNotificationHandler(NotificationMethods.ToolListChangedNotification,
                (_, _) => { Interlocked.Increment(ref session.CatalogVersion); return ValueTask.CompletedTask; });
            if (_configureSession is not null)
            {
                await _configureSession(session.Client, identity, cancellationToken).ConfigureAwait(false);
            }
        }

        var version = Volatile.Read(ref session.CatalogVersion);
        if (session.Tools is null || session.LoadedVersion != version)
        {
            var tools = await session.Client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            session.Tools = tools.ToDictionary(tool => tool.Name, StringComparer.Ordinal);
            session.LoadedVersion = version;
        }
    }

    private Task<McpClient> ConnectStdioAsync(TIdentity identity, CancellationToken cancellationToken)
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = _connection.Name,
            Command = _connection.Command,
            Arguments = [.. _connection.Arguments],
            WorkingDirectory = _connection.WorkingDirectory,
            ShutdownTimeout = TimeSpan.FromSeconds(2),
        });
        return McpClient.CreateAsync(transport,
            new McpClientOptions { InitializationTimeout = _connection.OperationTimeout },
            cancellationToken: cancellationToken);
    }

    private JsonElement SerializeBounded(CallToolResult result)
    {
        // Bound model/inspector evidence, not the server's business schema. Never return partial JSON
        // as if it were the full structured result, and retain the provider's isError value.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(result, McpJsonUtilities.DefaultOptions);
        if (bytes.Length <= _connection.ResponseBudgetBytes)
        {
            using var document = JsonDocument.Parse(bytes);
            return document.RootElement.Clone();
        }

        var bounded = new CallToolResult
        {
            IsError = result.IsError,
            Content = [new TextContentBlock { Text = "MCP response content was omitted because it exceeded the configured response budget. Request a narrower result; no complete evidence is available from this response." }],
            Meta = new JsonObject
            {
                ["digitalbrain"] = new JsonObject { ["truncated"] = true, ["responseBytes"] = bytes.Length },
            },
        };
        return JsonSerializer.SerializeToElement(bounded, McpJsonUtilities.DefaultOptions);
    }

    private async Task MaintainAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Math.Clamp(_connection.IdleTimeout.TotalMilliseconds, 100, 60_000)));
        try
        {
            while (await timer.WaitForNextTickAsync(_shutdown.Token).ConfigureAwait(false))
            {
                await PruneAsync(_shutdown.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
    }

    private static bool SameSchema(JsonElement? left, JsonElement? right)
        => left is null ? right is null : right is not null && JsonElement.DeepEquals(left.Value, right.Value);

    private sealed class Session
    {
        internal readonly SemaphoreSlim Gate = new(1, 1);
        internal McpClient? Client;
        internal IAsyncDisposable? Notification;
        internal Dictionary<string, McpClientTool>? Tools;
        internal long CatalogVersion;
        internal long LoadedVersion;
        internal int Users;
        internal DateTimeOffset LastUsed = DateTimeOffset.UtcNow;

        internal async ValueTask ResetAsync()
        {
            Tools = null;
            if (Notification is not null)
            {
                await Notification.DisposeAsync().ConfigureAwait(false);
                Notification = null;
            }

            var client = Client;
            Client = null;
            if (client is not null)
            {
                try { await client.DisposeAsync().ConfigureAwait(false); }
                catch (Exception) { /* Disposal of a failed transport must not replace its failure. */ }
            }
        }
    }
}
