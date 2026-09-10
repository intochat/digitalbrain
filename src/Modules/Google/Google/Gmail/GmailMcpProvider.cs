using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Client;

namespace DigitalBrain.Google;

internal sealed class GmailMcpProvider : IGmailProvider
{
    internal static readonly string[] NativeTools = ["search_threads", "get_thread", "list_labels", "create_draft"];

    public async Task<JsonElement> InvokeAsync(string tool, IReadOnlyDictionary<string, object?> arguments,
        string accessToken, CancellationToken cancellationToken)
    {
        if (!NativeTools.Contains(tool, StringComparer.Ordinal))
        {
            throw new GmailUnavailableException("This Gmail operation is not allowed.");
        }
        var normalized = GmailContent.Normalize(tool, arguments);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await using var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = GoogleModule.GmailMcpEndpoint,
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + accessToken },
            });
            await using var client = await McpClient.CreateAsync(transport, cancellationToken: timeout.Token).ConfigureAwait(false);
            var catalog = await client.ListToolsAsync(cancellationToken: timeout.Token).ConfigureAwait(false);
            ValidateCatalog(catalog);
            var result = await client.CallToolAsync(tool, normalized, cancellationToken: timeout.Token).ConfigureAwait(false);
            if (result.IsError == true)
            {
                throw new GmailUnavailableException("Gmail did not return complete successful evidence. Narrow the request or check service access.");
            }
            var envelope = JsonSerializer.SerializeToElement(result, McpJsonUtilities.DefaultOptions);
            if (Encoding.UTF8.GetByteCount(envelope.GetRawText()) > 1048576)
            {
                throw new GmailUnavailableException("Gmail response exceeds the provider response budget.");
            }
            return GmailContent.Project(tool, ReadContent(envelope), normalized);
        }
        catch (GmailUnavailableException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            throw new GmailUnavailableException("Gmail is unavailable. Check service access and try again later.");
        }
    }

    public async Task<string> ReadToolSchemaHashAsync(string tool, string accessToken, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!NativeTools.Contains(tool, StringComparer.Ordinal))
        {
            throw new GmailUnavailableException("This Gmail operation is not allowed.");
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await using var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = GoogleModule.GmailMcpEndpoint,
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + accessToken },
            });
            await using var client = await McpClient.CreateAsync(transport, cancellationToken: timeout.Token).ConfigureAwait(false);
            var catalog = await client.ListToolsAsync(cancellationToken: timeout.Token).ConfigureAwait(false);
            ValidateCatalog(catalog);
            var selected = catalog.Single(item => item.Name == tool);
            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(selected.JsonSchema.GetRawText())));
        }
        catch (GmailUnavailableException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            throw new GmailUnavailableException("Gmail is unavailable. Check service access and try again later.");
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
        throw new GmailUnavailableException("Gmail MCP returned an invalid response shape.");
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
                throw new GmailUnavailableException("The hosted Gmail MCP catalog is incompatible with the supported schema.");
            }
            if (enumField is not null && (!properties.GetProperty(enumField).TryGetProperty("enum", out var choices)
                || values!.Any(v => !choices.EnumerateArray().Any(c => c.GetString() == v))))
            {
                throw new GmailUnavailableException("The hosted Gmail MCP catalog does not support the required safe content format.");
            }
            foreach (var field in fields)
            {
                var schema = properties.GetProperty(field);
                var expectedType = field switch { "pageSize" => "integer", "includeTrash" => "boolean", "to" or "cc" or "bcc" => "array", _ => "string" };
                if (!schema.TryGetProperty("type", out var type) || type.GetString() != expectedType)
                {
                    throw new GmailUnavailableException("The hosted Gmail MCP argument types changed; review the provider policy.");
                }
                if (expectedType == "array" && (!schema.TryGetProperty("items", out var item)
                    || !item.TryGetProperty("type", out var itemType) || itemType.GetString() != "string"))
                {
                    throw new GmailUnavailableException("The hosted Gmail MCP recipient schema is incompatible.");
                }
            }
        }
    }

}
