using DigitalBrain.Abstractions;

namespace DigitalBrain.Modules.Sdk.Mcp;

[GenerateSerializer]
[Alias("db.mcp.authorization-denied-exception")]
[SettledDeliveryFailure]
public sealed class McpAuthorizationDeniedException : Exception
{
    public McpAuthorizationDeniedException()
        : this("MCP authorization was denied.")
    {
    }

    public McpAuthorizationDeniedException(string message)
        : base(message)
    {
    }

    public McpAuthorizationDeniedException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    public McpAuthorizationDeniedException(AuthorizationDenied denial)
        : base(BuildMessage(denial))
    {
        ArgumentNullException.ThrowIfNull(denial);
        Denial = denial;
    }

    [Id(0)]
    public AuthorizationDenied? Denial { get; set; }

    private static string BuildMessage(AuthorizationDenied denial)
    {
        ArgumentNullException.ThrowIfNull(denial);
        return $"Authorization for '{denial.ServerKey}' was denied for command '{denial.CommandId}'.";
    }
}
