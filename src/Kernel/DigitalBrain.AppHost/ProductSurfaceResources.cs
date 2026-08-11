internal static class ProductSurfaceResources
{
    public const string Brain = "brain";

    public const string Kernel = "kernel";

    // Generates a single-file C# brain client, runs it, exits. Completed is expected.
    public const string Scripting = "scripting";

    public const string Mcp = "mcp";
    public const string McpHttpEndpointName = "mcp";
    public const string McpPath = "/mcp";
    public const int McpHttpPort = 5000;

    public const int UiHttpPort = 5080;

    // Composed from the UI port + the kernel's actual callback path so a stale
    // /oauth/mcp/callback secret cannot become the accidental product default.
    public static string LocalDevelopmentOAuthCallbackUri { get; } =
        $"http://localhost:{UiHttpPort}{DigitalBrain.Abstractions.OAuthCallbackPaths.RelativePath}";
}
