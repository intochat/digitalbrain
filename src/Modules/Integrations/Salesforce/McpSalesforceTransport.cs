using System.Text.Json;
using DigitalBrain.Integrations.Mcp;

namespace DigitalBrain.Integrations.Salesforce;

public sealed class McpSalesforceTransport(
    IMcpIntegrationClient client,
    McpIntegrationEndpoint endpoint) : ISalesforceTransport
{
    public async Task<string> GetUserInfoJsonAsync(CancellationToken cancellationToken)
    {
        var result = await client.CallAsync(
            endpoint,
            "getUserInfo",
            new Dictionary<string, object?>(),
            cancellationToken).ConfigureAwait(false);
        return result.GetRawText();
    }

    public async Task<string> QueryJsonAsync(string query, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var result = await client.CallAsync(
            endpoint,
            "soqlQuery",
            new Dictionary<string, object?> { ["query"] = query },
            cancellationToken).ConfigureAwait(false);
        // Keep the records envelope used by SmartPrompt when hosted MCP returns an array.
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
        if (string.Equals(endpoint.Name, "salesforce", StringComparison.OrdinalIgnoreCase)
            && (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("body", out var fields)
                || fields.ValueKind != JsonValueKind.Object))
        {
            throw new ArgumentException(
                "Salesforce mutations require a JSON envelope with a body object, optional id, and explicit confirmed flag.",
                nameof(payloadJson));
        }
        var body = root.TryGetProperty("body", out var bodyElement) ? bodyElement.Clone() : root.Clone();
        var arguments = new Dictionary<string, object?>
        {
            [endpoint.Name.StartsWith("fake-", StringComparison.OrdinalIgnoreCase)
                ? "sobjectName"
                : "sobject-name"] = objectType,
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
        if (string.Equals(endpoint.Name, "salesforce", StringComparison.OrdinalIgnoreCase)
            && (!root.TryGetProperty("confirmed", out var confirmed) || confirmed.ValueKind != JsonValueKind.True))
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

        var result = await client.CallAsync(endpoint, tool, arguments, cancellationToken).ConfigureAwait(false);
        return result.GetRawText();
    }
}
