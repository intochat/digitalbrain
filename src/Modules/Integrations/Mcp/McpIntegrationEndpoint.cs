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

        ValidateSalesforceUri(uri);
        if (accessToken is null)
        {
            // Interactive OAuth is configured by IntegrationsModule. No fake fallback.
            return;
        }

        // Fail without echoing the credential, including malformed input. Only send it to Salesforce.
        if (string.IsNullOrWhiteSpace(accessToken) || accessToken.Any(static c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            throw new InvalidOperationException("Salesforce MCP requires a bearer token without whitespace or a Bearer prefix.");
        }
        _authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    internal static void ValidateSalesforceUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != "https" || uri.Host != "api.salesforce.com"
            || !uri.IsDefaultPort || !uri.AbsolutePath.StartsWith("/platform/mcp/", StringComparison.Ordinal)
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
        {
            throw new InvalidOperationException("Salesforce MCP requires an HTTPS hosted MCP endpoint on api.salesforce.com.");
        }
    }

    public string Name { get; }

    public Uri Uri { get; }

    internal void ConfigureHttpClient(HttpClient client)
        => client.DefaultRequestHeaders.Authorization = _authorization;

    public override string ToString() => $"{Name}: {Uri}";
}
