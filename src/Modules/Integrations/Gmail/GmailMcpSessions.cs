using System.Net;
using System.Text.Json;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Interactions;
using DigitalBrain.Integrations.Mcp;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DigitalBrain.Integrations.Gmail;

internal sealed class GmailMcpSessions(GmailConnections connections, IUntrustedContentScreen screen) : IAsyncDisposable
{
    private readonly Dictionary<OwnerId, Slot> _slots = [];
    internal async Task<JsonElement> CallAsync(OwnerId owner, McpIntegrationEndpoint endpoint, string tool,
        IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken, GmailIdentity? expectedIdentity = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var ct = deadline.Token;
        ct.ThrowIfCancellationRequested();
        McpIntegrationEndpoint.ValidateGmailUri(endpoint.Uri);
        GmailContent.ValidateArguments(tool, arguments);
        if (tool == "create_draft" && expectedIdentity is null)
        {
            throw new GmailOperationException("Draft writes require a consumed trusted user preview.");
        }
        var identity = connections.Identity(owner); // No network until an owner credential exists.
        if (expectedIdentity is not null && identity != expectedIdentity)
        {
            throw new GmailOperationException("The Gmail connection changed. Request a fresh preview.");
        }

        if (tool == "create_draft" && !identity.CanCompose)
        {
            throw new GmailAuthenticationRequiredException();
        }

        await screen.ScreenAsync(JsonSerializer.Serialize(arguments), ct).ConfigureAwait(false);
        Slot slot;
        lock (_slots)
        {
            if (!_slots.TryGetValue(owner, out slot!))
            {
                if (_slots.Count >= 128)
                {
                    throw new GmailOperationException("Gmail session capacity reached. Restart the kernel to clear unused connections.");
                }

                _slots.Add(owner, slot = new Slot());
            }
        }
        await slot.Gate.WaitAsync(ct).ConfigureAwait(false);
        var succeeded = false;
        var budget = new GmailBearerHandler.ResponseBudget();
        try
        {
            if (slot.Identity != identity || slot.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                await slot.ResetAsync().ConfigureAwait(false);
            }

            slot.Handler?.BeginOperation(budget);
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    _ = await connections.AccessTokenAsync(owner, identity, false, ct).ConfigureAwait(false);
                    if (slot.Client is null)
                    {
                        slot.Identity = identity; slot.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
                        slot.Handler = new GmailBearerHandler(connections, owner, identity);
                        slot.Handler.BeginOperation(budget);
                        slot.Http = new HttpClient(slot.Handler) { Timeout = TimeSpan.FromSeconds(30) };
                        var transport = new HttpClientTransport(new HttpClientTransportOptions
                        {
                            Endpoint = endpoint.Uri,
                            TransportMode = HttpTransportMode.StreamableHttp,
                            EnableStandaloneGetStream = false,
                            MaxReconnectionAttempts = 0,
                        }, slot.Http);
                        slot.Client = await McpClient.CreateAsync(transport, cancellationToken: ct).ConfigureAwait(false);
                        var tools = await slot.Client.ListToolsAsync(cancellationToken: ct).ConfigureAwait(false);
                        ValidateCatalog(tools);
                    }
                    var result = await slot.Client.CallToolAsync(tool, arguments, cancellationToken: ct).ConfigureAwait(false);
                    if (result.IsError == true)
                    {
                        throw new GmailOperationException("Gmail MCP reported a tool error. Check account eligibility, API/admin permissions and request arguments.");
                    }

                    JsonElement raw;
                    if (result.StructuredContent is JsonElement structured)
                    {
                        raw = structured;
                    }
                    else
                    {
                        var text = string.Join('\n', result.Content.OfType<TextContentBlock>().Select(b => b.Text));
                        using var document = JsonDocument.Parse(text); raw = document.RootElement.Clone();
                    }
                    var projected = GmailContent.Project(tool, raw, arguments);
                    await screen.ScreenAsync(projected.GetRawText(), ct).ConfigureAwait(false);
                    if (connections.Identity(owner) != identity)
                    {
                        throw new GmailOperationException("The Gmail connection changed during the request. Start a new request.");
                    }
                    succeeded = true;
                    return projected;
                }
                catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.Unauthorized)
                {
                    await slot.ResetAsync().ConfigureAwait(false);
                    if (tool == "create_draft" || attempt != 0)
                    {
                        await connections.RejectAsync(owner, identity, ct).ConfigureAwait(false);
                        throw new GmailAuthenticationRequiredException();
                    }
                    _ = await connections.AccessTokenAsync(owner, identity, true, ct).ConfigureAwait(false);
                }
            }
        }
        catch (GmailAuthenticationRequiredException) { throw; }
        catch (GmailOperationException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (HttpRequestException error)
        { throw new GmailOperationException(error.StatusCode is { } status ? $"Gmail MCP failed (HTTP {(int)status}). Check account permissions or try again later." : "Gmail MCP could not be reached. Try again later."); }
        catch (Exception) { throw new GmailOperationException("Gmail MCP or content screening failed. Narrow the request and check service access; no unsafe content was returned."); }
        finally
        {
            if (!succeeded)
            {
                await slot.ResetAsync().ConfigureAwait(false);
            }
            slot.Gate.Release();
        }
    }
    private static void ValidateCatalog(IEnumerable<McpClientTool> tools)
    {
        var catalog = tools.ToDictionary(t => t.Name, StringComparer.Ordinal);
        Check("search_threads", ["query", "pageSize", "pageToken", "includeTrash", "view"], "view", ["THREAD_VIEW_MINIMAL"]);
        Check("get_thread", ["threadId", "messageFormat"], "messageFormat", ["MINIMAL", "PLAIN_TEXT"]);
        Check("list_labels", []);
        Check("create_draft", ["to", "cc", "bcc", "subject", "body"]);
        void Check(string name, string[] fields, string? enumField = null, string[]? values = null)
        {
            if (!catalog.TryGetValue(name, out var tool) || !tool.JsonSchema.TryGetProperty("properties", out var properties)
                || fields.Any(f => !properties.TryGetProperty(f, out _)))
            {
                throw new GmailOperationException("The hosted Gmail MCP catalog is incompatible with the supported schema.");
            }

            if (enumField is not null && (!properties.GetProperty(enumField).TryGetProperty("enum", out var choices)
                || values!.Any(v => !choices.EnumerateArray().Any(c => c.GetString() == v))))
            {
                throw new GmailOperationException("The hosted Gmail MCP catalog does not support the required safe content format.");
            }
            foreach (var field in fields)
            {
                var schema = properties.GetProperty(field);
                var expectedType = field switch { "pageSize" => "integer", "includeTrash" => "boolean", "to" or "cc" or "bcc" => "array", _ => "string" };
                if (!schema.TryGetProperty("type", out var type) || type.GetString() != expectedType)
                {
                    throw new GmailOperationException("The hosted Gmail MCP argument types changed; access is blocked until the mapping is reviewed.");
                }
                if (expectedType == "array" && (!schema.TryGetProperty("items", out var item)
                    || !item.TryGetProperty("type", out var itemType) || itemType.GetString() != "string"))
                {
                    throw new GmailOperationException("The hosted Gmail MCP recipient schema is incompatible.");
                }
            }
        }
    }

    internal async Task PruneAsync()
    {
        Slot[] slots;
        lock (_slots) { slots = _slots.Values.ToArray(); }
        foreach (var slot in slots)
        {
            if (!slot.Gate.Wait(0)) { continue; }
            try
            {
                if (slot.ExpiresAt <= DateTimeOffset.UtcNow)
                {
                    await slot.ResetAsync().ConfigureAwait(false);
                }
            }
            finally { slot.Gate.Release(); }
        }
    }
    public async ValueTask DisposeAsync()
    {
        foreach (var slot in _slots.Values)
        {
            await slot.ResetAsync().ConfigureAwait(false);
        }

        _slots.Clear();
    }
    private sealed class Slot
    {
        internal readonly SemaphoreSlim Gate = new(1, 1);
        internal GmailIdentity? Identity;
        internal DateTimeOffset ExpiresAt;
        internal McpClient? Client;
        internal HttpClient? Http;
        internal GmailBearerHandler? Handler;
        internal async ValueTask ResetAsync()
        {
            // Cancel/dispose HTTP first: an expired session must not refresh credentials to DELETE itself.
            Http?.Dispose(); Http = null; Handler = null;
            if (Client is not null) { try { await Client.DisposeAsync().ConfigureAwait(false); } catch (Exception) { } Client = null; }
            Identity = null;
        }
    }
}
