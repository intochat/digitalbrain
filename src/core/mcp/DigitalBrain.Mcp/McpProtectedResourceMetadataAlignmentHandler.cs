using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DigitalBrain.Mcp;

// Google Workspace remote MCP hosts advertise PRM resource `https://{host}/mcp` while the
// documented MCP endpoint is `https://{host}/mcp/v1`. ModelContextProtocol 2.0.0 rejects that
// mismatch before OAuth starts. Align the metadata resource to the live endpoint path.
internal sealed class McpProtectedResourceMetadataAlignmentHandler : DelegatingHandler
{
    private const string WellKnownSegment = "/.well-known/oauth-protected-resource";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!ShouldInspect(request, response))
        {
            return response;
        }

        var payload = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (!TryAlign(payload, out var aligned))
        {
            response.Content = CreateJsonContent(payload, response.Content.Headers.ContentType);
            return response;
        }

        response.Content = CreateJsonContent(aligned, response.Content.Headers.ContentType);
        return response;
    }

    internal static bool TryAlign(ReadOnlySpan<byte> payload, out byte[] aligned)
    {
        aligned = [];
        try
        {
            var node = JsonNode.Parse(payload);
            if (node is not JsonObject root
                || root["resource"] is not JsonValue resourceNode
                || resourceNode.GetValueKind() is not JsonValueKind.String
                || resourceNode.GetValue<string>() is not { Length: > 0 } resourceText
                || !Uri.TryCreate(resourceText, UriKind.Absolute, out var resource))
            {
                return false;
            }

            var changed = false;
            if (TryMapGoogleWorkspaceResource(resource, out var expected)
                && !string.Equals(resource.AbsoluteUri, expected.AbsoluteUri, StringComparison.Ordinal))
            {
                root["resource"] = expected.AbsoluteUri;
                changed = true;
            }

            // Google PRM lists authorization_servers with a trailing slash while AS metadata
            // issuer is without one; MCP 2.0.0 requires exact RFC 8414 issuer match.
            if (TryNormalizeGoogleAuthorizationServers(root))
            {
                changed = true;
            }

            if (!changed)
            {
                return false;
            }

            aligned = Encoding.UTF8.GetBytes(root.ToJsonString(JsonOptions));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool ShouldInspect(HttpRequestMessage request, HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode
            || request.RequestUri is null
            || !request.RequestUri.IsAbsoluteUri)
        {
            return false;
        }

        var path = request.RequestUri.AbsolutePath;
        if (path.IndexOf(WellKnownSegment, StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        return mediaType is not null
            && mediaType.Contains("json", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryMapGoogleWorkspaceResource(Uri resource, out Uri expected)
    {
        expected = resource;
        if (!resource.Host.EndsWith(".googleapis.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Google publishes PRM resource .../mcp while clients connect to .../mcp/v1.
        var path = resource.AbsolutePath.TrimEnd('/');
        if (!string.Equals(path, "/mcp", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        expected = new UriBuilder(resource) { Path = "/mcp/v1", Query = string.Empty }.Uri;
        return true;
    }

    private static bool TryNormalizeGoogleAuthorizationServers(JsonObject root)
    {
        if (root["authorization_servers"] is not JsonArray servers || servers.Count == 0)
        {
            return false;
        }

        var changed = false;
        for (var index = 0; index < servers.Count; index++)
        {
            if (servers[index] is not JsonValue value
                || value.GetValueKind() is not JsonValueKind.String
                || value.GetValue<string>() is not { Length: > 0 } text
                || !Uri.TryCreate(text, UriKind.Absolute, out var server)
                || !string.Equals(server.Host, "accounts.google.com", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var normalized = server.GetLeftPart(UriPartial.Authority);
            if (string.Equals(text, normalized, StringComparison.Ordinal))
            {
                continue;
            }

            servers[index] = normalized;
            changed = true;
        }

        return changed;
    }

    private static ByteArrayContent CreateJsonContent(byte[] payload, MediaTypeHeaderValue? contentType)
    {
        var content = new ByteArrayContent(payload);
        content.Headers.ContentType = contentType is null
            ? new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" }
            : contentType;
        return content;
    }
}
