using System.Text.Json;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Product.Interactions;
using DigitalBrain.Sdk;
using ModelContextProtocol.Client;

namespace DigitalBrain.Google;

// Gmail policy around the SDK client: argument allow-list, draft identity rules, request and
// response screening, positive projection. Sessions, bearer auth and the read retry are the SDK's.
internal sealed class GmailMcp : IAsyncDisposable
{
    private readonly GmailConnections _connections;
    private readonly IUntrustedContentScreen _screen;
    private readonly McpToolClient<GmailIdentity> _client;

    public GmailMcp(GmailConnections connections, IUntrustedContentScreen screen)
    {
        _connections = connections;
        _screen = screen;
        _client = new McpToolClient<GmailIdentity>(
            new McpEndpoint("gmail", GoogleModule.GmailMcpEndpoint),
            connections,
            new McpToolPolicy(static tool => tool != "create_draft", ValidateCatalog),
            new McpSessionOptions
            {
                Lifetime = TimeSpan.FromMinutes(10),
                ResponseBudgetBytes = 1048576,
                Timeout = TimeSpan.FromSeconds(30),
            });
    }

    internal async Task<JsonElement> CallAsync(OwnerId owner, string tool,
        IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken, GmailIdentity? expectedIdentity = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var ct = deadline.Token;
        ct.ThrowIfCancellationRequested();
        GmailContent.ValidateArguments(tool, arguments);
        if (tool == "create_draft" && expectedIdentity is null)
        {
            throw new McpOperationException("Draft writes require a consumed trusted user preview.");
        }
        var identity = _connections.Identity(owner); // No network until an owner credential exists.
        if (expectedIdentity is not null && identity != expectedIdentity)
        {
            throw new McpOperationException("The Gmail connection changed. Request a fresh preview.");
        }

        if (tool == "create_draft" && !identity.CanCompose)
        {
            throw new McpAuthenticationRequiredException();
        }

        await _screen.ScreenAsync(JsonSerializer.Serialize(arguments), ct).ConfigureAwait(false);
        try
        {
            var raw = await _client.CallAsync(owner, tool, arguments, ct).ConfigureAwait(false);
            var projected = GmailContent.Project(tool, raw, arguments);
            await _screen.ScreenAsync(projected.GetRawText(), ct).ConfigureAwait(false);
            if (_connections.Identity(owner) != identity)
            {
                throw new McpOperationException("The Gmail connection changed during the request. Start a new request.");
            }
            return projected;
        }
        catch (McpAuthenticationRequiredException) { throw; }
        catch (McpOperationException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (HttpRequestException error)
        { throw new McpOperationException(error.StatusCode is { } status ? $"Gmail MCP failed (HTTP {(int)status}). Check account permissions or try again later." : "Gmail MCP could not be reached. Try again later."); }
        catch (Exception) { throw new McpOperationException("Gmail MCP or content screening failed. Narrow the request and check service access; no unsafe content was returned."); }
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
                throw new McpOperationException("The hosted Gmail MCP catalog is incompatible with the supported schema.");
            }

            if (enumField is not null && (!properties.GetProperty(enumField).TryGetProperty("enum", out var choices)
                || values!.Any(v => !choices.EnumerateArray().Any(c => c.GetString() == v))))
            {
                throw new McpOperationException("The hosted Gmail MCP catalog does not support the required safe content format.");
            }
            foreach (var field in fields)
            {
                var schema = properties.GetProperty(field);
                var expectedType = field switch { "pageSize" => "integer", "includeTrash" => "boolean", "to" or "cc" or "bcc" => "array", _ => "string" };
                if (!schema.TryGetProperty("type", out var type) || type.GetString() != expectedType)
                {
                    throw new McpOperationException("The hosted Gmail MCP argument types changed; access is blocked until the mapping is reviewed.");
                }
                if (expectedType == "array" && (!schema.TryGetProperty("items", out var item)
                    || !item.TryGetProperty("type", out var itemType) || itemType.GetString() != "string"))
                {
                    throw new McpOperationException("The hosted Gmail MCP recipient schema is incompatible.");
                }
            }
        }
    }

    internal Task PruneAsync() => _client.PruneAsync();

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
