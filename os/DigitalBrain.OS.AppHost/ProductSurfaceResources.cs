// Product packaging Option A (I1a): the digitalbrain product image supervises silo and
// behavior-host as separate child processes. Authored behavior assemblies load only in
// behavior-host; silo residual in-process execution stays closed.
// Docker: os/DigitalBrain.OS.Host/Dockerfile + docker-entrypoint.sh (AppHost stays local-only).
internal static class ProductSurfaceResources
{
    public const string Brain = "brain";

    // Supervised product process: Orleans silo + northbound MCP. Never loads authored assemblies.
    public const string Silo = "silo";

    // Supervised product process: signed authored behavior worker (deploy/activate/execute).
    public const string BehaviorHost = "behavior-host";

    public const string McpHttpEndpointName = "mcp";
    public const string McpPath = "/mcp";
    public const int McpHttpPort = 5000;

    // Stable host ports so Google/Salesforce OAuth redirect URIs need registering only once.
    // Must match DigitalBrain.Aspire.Hosting.LocalDevelopmentProductSurface.
    public const int UiHttpPort = 5080;
    public const string LocalDevelopmentOAuthCallbackUri = "http://localhost:5080/oauth/callback";
}
