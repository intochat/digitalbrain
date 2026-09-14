internal static class ProductSurfaceResources
{
    public const string Brain = "brain";

    // The gateway owns the one address the shell, .mcp.json and every MCP client use; the two slots of the
    // silo sit behind it and are never addressed directly.
    public const string Gateway = "gateway";

    public const string KernelA = "kernel-a";

    public const string KernelB = "kernel-b";

    public const int GatewayHttpPort = 5080;

    public const int KernelAHttpPort = 5081;

    public const int KernelBHttpPort = 5082;

    public const string GatewayUrl = "http://localhost:5080";

    public const string KernelAUrl = "http://localhost:5081";

    public const string KernelBUrl = "http://localhost:5082";
}
