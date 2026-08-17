using DigitalBrain.Abstractions;

namespace DigitalBrain.Modules.Sdk.Mcp;

[GenerateSerializer]
[Alias("db.mcp.authorization-claim-kind")]
public enum McpAuthorizationClaimKind
{
    Required = 0,
    Completed = 1,
    Denied = 2,
}

