namespace DigitalBrain.Testing;

public static class TestingEdges
{
    public const string ChatClient = "IChatClient";

    public const string SouthboundMcpTransport = "southbound MCP transport";

    public const string OAuthAndParams = "OAuth/params";

    public const string TimeProvider = "TimeProvider";

    public static readonly string[] Closed =
    [
        ChatClient,
        SouthboundMcpTransport,
        OAuthAndParams,
        TimeProvider,
    ];
}
