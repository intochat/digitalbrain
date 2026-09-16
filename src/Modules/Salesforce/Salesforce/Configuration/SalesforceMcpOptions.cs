namespace DigitalBrain.Salesforce;

public sealed class SalesforceMcpOptions
{
    public const string SectionName = "DigitalBrain:Salesforce:Mcp";
    public string? Endpoint { get; set; }

    internal Uri? ResolveEndpoint()
    {
        var value = Endpoint;
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
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
