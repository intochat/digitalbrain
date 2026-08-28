using System.Text.Json;
using DigitalBrain.Integrations.Mcp;

namespace DigitalBrain.Integrations.Salesforce;

public sealed class McpSalesforceTransport(
    IMcpIntegrationClient client,
    McpIntegrationEndpoint endpoint) : ISalesforceTransport
{
    public async Task<string> QueryJsonAsync(string query, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var result = await client.CallAsync(
            endpoint,
            "soqlQuery",
            new Dictionary<string, object?> { ["query"] = query },
            cancellationToken).ConfigureAwait(false);
        return result.GetRawText();
    }

    public async Task<string> UpsertJsonAsync(
        string objectType,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(payloadJson);
        var root = document.RootElement;
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

        var result = await client.CallAsync(endpoint, tool, arguments, cancellationToken).ConfigureAwait(false);
        return result.GetRawText();
    }
}
