using DigitalBrain.Core;

namespace DigitalBrain.Salesforce;

/// <summary>Public module settings. Credentials remain in the host's secret configuration.</summary>
public sealed record SalesforceModuleOptions : IModuleOptions
{
    public Uri? McpEndpoint { get; set; }
    public Uri? PublicOrigin { get; set; }
    public bool HostMcp { get; set; }
    public bool UseLocalMcp { get; set; }

    public SalesforceModuleOptions WithHostedMcp(Uri? endpoint = null)
    {
        HostMcp = true;
        McpEndpoint = endpoint;
        UseLocalMcp = false;
        return this;
    }

    public SalesforceModuleOptions WithLocalMcp(Uri endpoint)
    {
        HostMcp = true;
        McpEndpoint = endpoint;
        UseLocalMcp = true;
        return this;
    }

    public void Validate()
    {
        if (McpEndpoint is { IsAbsoluteUri: false } || PublicOrigin is { IsAbsoluteUri: false })
        { throw new ArgumentException("Salesforce endpoints must be absolute."); }
        if (UseLocalMcp && (McpEndpoint is not { IsLoopback: true } local || local.Scheme is not ("http" or "https")))
        { throw new ArgumentException("A local MCP endpoint must use loopback HTTP(S)."); }
    }
}
