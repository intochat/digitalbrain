using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Client;

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
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await using var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = endpoint,
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + accessToken },
            });
            await using var client = await McpClient.CreateAsync(transport, cancellationToken: timeout.Token).ConfigureAwait(false);
            var result = await client.CallToolAsync(tool, normalized, cancellationToken: timeout.Token).ConfigureAwait(false);
            var envelope = JsonSerializer.SerializeToElement(result, McpJsonUtilities.DefaultOptions);
            if (Encoding.UTF8.GetByteCount(envelope.GetRawText()) > 128 * 1024)
            {
                throw new SalesforceUnavailableException("Salesforce response exceeds the provider response budget.");
            }
            if (result.IsError == true)
            {
                if (tool is "createRecord" or "updateRecord")
                {
                    return envelope;
                }
                throw new SalesforceUnavailableException("Salesforce returned a provider error.");
            }
            return ReadContent(envelope);
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
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await using var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = endpoint,
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + accessToken },
            });
            await using var client = await McpClient.CreateAsync(transport, cancellationToken: timeout.Token).ConfigureAwait(false);
            var catalog = await client.ListToolsAsync(cancellationToken: timeout.Token).ConfigureAwait(false);
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
