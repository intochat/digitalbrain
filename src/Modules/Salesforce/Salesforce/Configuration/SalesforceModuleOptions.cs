namespace DigitalBrain.Salesforce;

/// <summary>Public module settings. Credentials remain in the host's secret configuration.</summary>
public sealed record SalesforceModuleOptions
{
    public Uri? McpEndpoint { get; init; }
}