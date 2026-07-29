using DigitalBrain.Abstractions;

namespace DigitalBrain.Mcp;

[GenerateSerializer]
[Alias("db.mcp.begin-authorization")]
public sealed record BeginMcpAuthorization(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string ServerKey,
    [property: Id(2)] string ServerDisplayName,
    [property: Id(3)] Uri SignInUrl,
    [property: Id(4)] string State);

[GenerateSerializer]
[Alias("db.mcp.deliver-authorization-callback")]
public sealed record DeliverMcpAuthorizationCallback(
    [property: Id(0)] string State,
    [property: Id(1)] string? Code,
    [property: Id(2)] string? Error,
    [property: Id(3)] string? Iss);

[GenerateSerializer]
[Alias("db.mcp.authorization-callback-delivery")]
public sealed record McpAuthorizationCallbackDelivery(
    [property: Id(0)] bool Accepted,
    [property: Id(1)] bool Completed,
    [property: Id(2)] bool Denied);

[GenerateSerializer]
[Alias("db.mcp.authorization-code-result")]
public sealed record McpAuthorizationCodeResult(
    [property: Id(0)] string Code,
    [property: Id(1)] string? Iss);

[GenerateSerializer]
[Alias("db.mcp.authorization-claim")]
public sealed record McpAuthorizationClaim(
    [property: Id(0)] McpAuthorizationClaimKind Kind,
    [property: Id(1)] AuthorizationRequired? Required,
    [property: Id(2)] AuthorizationDenied? Denied);

[GenerateSerializer]
[Alias("db.mcp.authorization-claim-kind")]
public enum McpAuthorizationClaimKind
{
    Required = 0,
    Completed = 1,
    Denied = 2,
}
