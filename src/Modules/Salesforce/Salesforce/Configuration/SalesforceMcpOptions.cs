namespace DigitalBrain.Salesforce;

public sealed class SalesforceMcpOptions
{
    public const string SectionName = "DigitalBrain:Salesforce:Mcp";
    public string? Endpoint { get; set; }
    public bool AllowLoopback { get; set; }

    internal Uri? ResolveEndpoint()
    {
        var value = Endpoint;
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (AllowLoopback && Uri.TryCreate(value, UriKind.Absolute, out var local) && local.IsLoopback
            && local.Scheme is "http" or "https" && local.UserInfo.Length == 0 && local.Query.Length == 0 && local.Fragment.Length == 0)
        {
            return local;
        }
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || uri.Host != "api.salesforce.com" || !uri.IsDefaultPort
            || !uri.AbsolutePath.StartsWith("/platform/mcp/", StringComparison.Ordinal)
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
        {
            throw new InvalidOperationException(
                $"Configuration '{SalesforceModule.McpEndpointConfigurationKey}' must be an HTTPS hosted MCP endpoint on api.salesforce.com.");
        }

        return uri;
    }
}
