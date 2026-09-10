using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DigitalBrain.Core;
using ModelContextProtocol;

namespace DigitalBrain.Salesforce;

internal sealed class SalesforceMcpProvider(Uri? endpoint) : ISalesforceProvider
{
    internal static readonly string[] NativeTools = ["getUserInfo", "soqlQuery", "createRecord", "updateRecord"];

    public async Task<JsonElement> InvokeAsync(string tool, JsonElement arguments,
        string accessToken, CancellationToken cancellationToken)
    {
        if (!NativeTools.Contains(tool, StringComparer.Ordinal))
        {
            throw new SalesforceUnavailableException("This Salesforce operation is not allowed.");
        }
        SalesforceTokenRefresh.ValidateToken(accessToken);
        if (endpoint is null)
        {
            throw new SalesforceUnavailableException("Salesforce MCP is not configured.");
        }
        var normalized = arguments.EnumerateObject().ToDictionary(property => property.Name, property => (object?)property.Value.Clone());
        try
        {
            return await McpHttpSession.RunAsync(endpoint, accessToken, async (client, token) =>
            {
                var result = await client.CallToolAsync(tool, normalized, cancellationToken: token).ConfigureAwait(false);
                var envelope = JsonSerializer.SerializeToElement(result, McpJsonUtilities.DefaultOptions);
                if (Encoding.UTF8.GetByteCount(envelope.GetRawText()) > 128 * 1024)
                {
                    throw new SalesforceUnavailableException("Salesforce response exceeds the provider response budget.");
                }
                if (result.IsError == true)
                {
                    if (tool is "createRecord" or "updateRecord")
                    {
                        return MarkUntrusted(envelope);
                    }
                    throw new SalesforceUnavailableException("Salesforce returned a provider error.");
                }
                return MarkUntrusted(ReadContent(envelope));
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (SalesforceUnavailableException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            throw new SalesforceUnavailableException("Salesforce is unavailable. Check service access and try again later.");
        }
    }

    public async Task<string> ReadToolSchemaHashAsync(string tool, string accessToken, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!NativeTools.Contains(tool, StringComparer.Ordinal))
        {
            throw new SalesforceUnavailableException("This Salesforce operation is not allowed.");
        }
        SalesforceTokenRefresh.ValidateToken(accessToken);
        if (endpoint is null)
        {
            throw new SalesforceUnavailableException("Salesforce MCP is not configured.");
        }
        try
        {
            var catalog = await McpHttpSession.ReadCatalogAsync(endpoint, accessToken, cancellationToken).ConfigureAwait(false);
            var selected = catalog.SingleOrDefault(item => item.Name == tool)
                ?? throw new SalesforceUnavailableException("The authenticated Salesforce catalog does not contain this tool.");
            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
                selected.JsonSchema.GetRawText() + "\n" + selected.ReturnJsonSchema?.GetRawText())));
        }
        catch (SalesforceUnavailableException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            throw new SalesforceUnavailableException("Salesforce is unavailable. Check service access and try again later.");
        }
    }

    private static JsonElement MarkUntrusted(JsonElement content)
    {
        if (content.ValueKind != JsonValueKind.Object)
        {
            throw new SalesforceUnavailableException("Salesforce MCP returned an invalid response shape.");
        }
        var result = JsonNode.Parse(content.GetRawText())!.AsObject();
        // screened at the NativeTools boundary (AI module)
        result["untrustedData"] = true;
        return JsonSerializer.SerializeToElement(result);
    }

    private static JsonElement ReadContent(JsonElement envelope)
    {
        if (envelope.TryGetProperty("structuredContent", out var structured))
        {
            return structured;
        }
        if (envelope.TryGetProperty("content", out var blocks) && blocks.ValueKind == JsonValueKind.Array)
        {
            var text = string.Join('\n', blocks.EnumerateArray()
                .Where(static block => block.TryGetProperty("type", out var type) && type.GetString() == "text")
                .Select(static block => block.GetProperty("text").GetString()));
            try { using var document = JsonDocument.Parse(text); return document.RootElement.Clone(); }
            catch (JsonException) { }
        }
        throw new SalesforceUnavailableException("Salesforce MCP returned an invalid response shape.");
    }
}
