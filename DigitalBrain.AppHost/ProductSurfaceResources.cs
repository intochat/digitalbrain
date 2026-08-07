// Product packaging: the digitalbrain image supervises silo, mcp, and behavior-host as
// separate child processes. Authored assemblies load only in behavior-host; northbound MCP
// is a cluster client. Docker: DigitalBrain.Kernel/Dockerfile + docker-entrypoint.sh.
internal static class ProductSurfaceResources
{
    public const string Brain = "brain";

    // Supervised product process: Orleans silo. Never loads authored assemblies; no northbound MCP.
    public const string Silo = "silo";

    // Supervised product process: signed authored behavior worker (deploy/activate/execute).
    public const string BehaviorHost = "behavior-host";

    // Northbound MCP: cluster client only — tools talk through IDigitalBrain, not the silo process.
    public const string Mcp = "mcp";
    public const string McpHttpEndpointName = "mcp";
    public const string McpPath = "/mcp";
    public const int McpHttpPort = 5000;

    // Stable host port so Google/Salesforce OAuth redirect URIs need registering only once.
    // This product owns the number; the packable hosting packages take it as a parameter.
    public const int UiHttpPort = 5080;
    public const string LocalDevelopmentOAuthCallbackUri = "http://localhost:5080/oauth/callback";
}
