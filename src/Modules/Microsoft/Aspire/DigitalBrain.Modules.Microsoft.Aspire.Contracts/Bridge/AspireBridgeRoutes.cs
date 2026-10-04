namespace DigitalBrain.Microsoft.Aspire;

public static class AspireBridgeRoutes
{
    public const string Root = "/aspire/bridge";
    public const string Commands = Root + "/commands";
    public const string Resources = Root + "/resources";
    public const string ResourceSync = Root + "/resources/sync";
    public const string KeyHeader = "X-DigitalBrain-Aspire-Key";

    public static string CommandResult(Guid id) => Commands + "/" + id.ToString("D");
}
