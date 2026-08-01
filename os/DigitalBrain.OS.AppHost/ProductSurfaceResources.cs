// Product packaging Option A (I1a): the digitalbrain product image supervises silo and
// behavior-host as separate child processes. Authored behavior assemblies load only in
// behavior-host; silo residual in-process execution stays closed. Docker entrypoint: Wave 11.
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
}
