using DigitalBrain.Abstractions;

namespace DigitalBrain.Modules.Sdk.Mcp;

[GenerateSerializer]
[Alias("db.mcp.begin-authorization")]
public sealed record BeginMcpAuthorization(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string ServerKey,
    [property: Id(2)] string ServerDisplayName,
    [property: Id(3)] Uri SignInUrl,
    [property: Id(4)] string State,
    [property: Id(5)] ActorContext Actor,
    [property: Id(6)] string? CodeChallenge = null,
    [property: Id(7)] string? CodeVerifier = null);

