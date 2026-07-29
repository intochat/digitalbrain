namespace DigitalBrain.Mcp;

[GenerateSerializer]
[Alias("db.mcp.authorization-required-exception")]
public sealed class McpAuthorizationRequiredException : Exception
{
    public McpAuthorizationRequiredException()
        : this("MCP authorization is required before the operation can continue.")
    {
    }

    public McpAuthorizationRequiredException(string message)
        : base(message)
    {
    }

    public McpAuthorizationRequiredException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    public McpAuthorizationRequiredException(AuthorizationRequired requirement)
        : base(BuildMessage(requirement))
    {
        ArgumentNullException.ThrowIfNull(requirement);
        Requirement = requirement;
    }

    [Id(0)]
    public AuthorizationRequired? Requirement { get; set; }

    private static string BuildMessage(AuthorizationRequired requirement)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        return $"{requirement.ServerDisplayName} requires sign-in before the operation can continue.";
    }
}
