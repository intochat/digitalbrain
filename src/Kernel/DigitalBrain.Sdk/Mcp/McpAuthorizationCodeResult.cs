using DigitalBrain.Abstractions;

namespace DigitalBrain.Modules.Sdk.Mcp;

[GenerateSerializer]
[Alias("db.mcp.authorization-code-result")]
public sealed record McpAuthorizationCodeResult(
    [property: Id(0)] string Code,
    [property: Id(1)] string? Iss,
    [property: Id(2)] string? CodeVerifier = null,
    [property: Id(3)] ActorContext? Actor = null);

