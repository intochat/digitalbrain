using System.Text.Json;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Sdk;

namespace DigitalBrain.Salesforce;

internal sealed class McpSalesforce : ISalesforce, IAsyncDisposable
{
    private static readonly string[] ReadTools = ["getUserInfo", "soqlQuery"];

    private readonly SalesforceConnections _connections;
    private readonly McpToolClient<OwnerId> _client;

    public McpSalesforce(McpEndpoint endpoint, SalesforceConnections connections)
    {
        _connections = connections;
        _client = new McpToolClient<OwnerId>(
            endpoint,
            connections,
            new McpToolPolicy(static tool => ReadTools.Contains(tool, StringComparer.Ordinal)));
    }

    public async Task<string> GetUserInfoJsonAsync(CancellationToken cancellationToken)
    {
        var result = await CallAsync("getUserInfo", new Dictionary<string, object?>(), cancellationToken).ConfigureAwait(false);
        return result.GetRawText();
    }

    public async Task<string> QueryJsonAsync(string query, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var result = await CallAsync("soqlQuery", new Dictionary<string, object?> { ["query"] = query }, cancellationToken).ConfigureAwait(false);
        // Keep the records envelope callers expect when hosted MCP returns an array.
        if (result.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Serialize(new { totalSize = result.GetArrayLength(), records = result });
        }
        return result.GetRawText();
    }

    public async Task<string> UpsertJsonAsync(
        string objectType,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var document = JsonDocument.Parse(payloadJson);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("body", out var fields)
            || fields.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException(
                "Salesforce mutations require a JSON envelope with a body object, optional id, and explicit confirmed flag.",
                nameof(payloadJson));
        }
        var body = fields.Clone();
        var arguments = new Dictionary<string, object?>
        {
            ["sobject-name"] = objectType,
            ["body"] = body,
        };

        var tool = "createRecord";
        if (root.TryGetProperty("id", out var idElement)
            && idElement.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(idElement.GetString()))
        {
            tool = "updateRecord";
            arguments["id"] = idElement.GetString();
        }

        // Enforce approval for every real caller, including Experience capability handlers.
        // Confirmation is local control data and is never sent as a Salesforce field.
        if (!root.TryGetProperty("confirmed", out var confirmed) || confirmed.ValueKind != JsonValueKind.True)
        {
            return JsonSerializer.Serialize(new
            {
                confirmationRequired = true,
                message = "No Salesforce mutation was made. Ask the user to confirm these exact changes.",
                operation = tool,
                objectType,
                id = arguments.GetValueOrDefault("id"),
                body,
            });
        }

        var result = await CallAsync(tool, arguments, cancellationToken).ConfigureAwait(false);
        return result.GetRawText();
    }

    // Known-disconnected is control flow inside the SDK client: no unauthenticated HTTP leaves.
    private Task<JsonElement> CallAsync(string tool, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
        => _client.CallAsync(_connections.CurrentOwner, tool, arguments, cancellationToken);

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
