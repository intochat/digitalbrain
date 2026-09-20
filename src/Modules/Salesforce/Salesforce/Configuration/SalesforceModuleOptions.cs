namespace DigitalBrain.Salesforce;

/// <summary>Public module settings. Credentials remain in the host's secret configuration.</summary>
public sealed record SalesforceModuleOptions
{
    public Uri? McpEndpoint { get; set; }
    public Uri? PublicOrigin { get; set; }
    public bool HostMcp { get; set; }
    public bool UseLocalMcp { get; set; }
}
