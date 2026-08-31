using System.Net.Http.Headers;

namespace DigitalBrain.Integrations.Mcp;

// Credentials remain private, outside record formatting and JSON serialization.
public sealed class McpIntegrationEndpoint
{
    private readonly AuthenticationHeaderValue? _authorization;

    public McpIntegrationEndpoint(string name, Uri uri, string? accessToken = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(uri);
        Name = name;
        Uri = uri;

        if (!string.Equals(name, "salesforce", StringComparison.OrdinalIgnoreCase))
        {
            if (accessToken is not null)
            {
                throw new InvalidOperationException("Bearer authentication is only supported for Salesforce MCP.");
            }
            return;
        }

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException(
                "Salesforce MCP has an endpoint but no access token. Supply the required "
                + "salesforce-access-token secret (DigitalBrain:Integrations:Salesforce:Mcp:AccessToken).");
        }

        // Fail without echoing the credential, including malformed input. Only send it to Salesforce.
        if (accessToken.Any(static c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            throw new InvalidOperationException("Salesforce MCP requires a bearer token without whitespace or a Bearer prefix.");
        }
        if (!uri.IsAbsoluteUri || uri.Scheme != "https" || uri.Host != "api.salesforce.com"
            || !uri.IsDefaultPort || !uri.AbsolutePath.StartsWith("/platform/mcp/", StringComparison.Ordinal)
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
        {
            throw new InvalidOperationException("Salesforce MCP requires an HTTPS hosted MCP endpoint on api.salesforce.com.");
        }

        _authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    public string Name { get; }

    public Uri Uri { get; }

    internal void ConfigureHttpClient(HttpClient client)
        => client.DefaultRequestHeaders.Authorization = _authorization;

    public override string ToString() => $"{Name}: {Uri}";
}
