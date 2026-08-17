using DigitalBrain.Abstractions;

namespace DigitalBrain.Modules.Sdk.Mcp;

[GenerateSerializer]
[Alias("db.mcp.authorization-claim")]
public sealed record McpAuthorizationClaim(
    [property: Id(0)] McpAuthorizationClaimKind Kind,
    [property: Id(1)] AuthorizationRequired? Required,
    [property: Id(2)] AuthorizationDenied? Denied);

