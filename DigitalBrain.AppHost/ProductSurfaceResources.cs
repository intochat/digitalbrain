// Product processes: silo, northbound MCP, and a dummy scripting worker.
// Docker: DigitalBrain.Kernel/Dockerfile + docker-entrypoint.sh (AppHost stays local-only).
internal static class ProductSurfaceResources
{
    public const string Brain = "brain";

    // Orleans silo. Never loads authored assemblies; no northbound MCP.
    public const string Silo = "silo";

    // Dummy external worker: generate a single-file C# brain client and print its reply.
    public const string Scripting = "scripting";

    // Northbound MCP: cluster client only — tools talk through IDigitalBrain, not the silo process.
    public const string Mcp = "mcp";
    public const string McpHttpEndpointName = "mcp";
    public const string McpPath = "/mcp";
    public const int McpHttpPort = 5000;

    // Stable host port so Google/Salesforce OAuth redirect URIs need registering only once.
    public const int UiHttpPort = 5080;
    public const string LocalDevelopmentOAuthCallbackUri = "http://localhost:5080/oauth/callback";
}
