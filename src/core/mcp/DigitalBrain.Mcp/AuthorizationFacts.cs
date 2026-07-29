using DigitalBrain.Abstractions;

namespace DigitalBrain.Mcp;

[GenerateSerializer]
[Alias("db.mcp.authorization-required")]
public sealed record AuthorizationRequired(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string ServerKey,
    [property: Id(2)] string ServerDisplayName,
    [property: Id(3)] Uri SignInUrl,
    [property: Id(4)] string State) : Synapse;

[GenerateSerializer]
[Alias("db.mcp.authorization-completed")]
public sealed record AuthorizationCompleted(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string ServerKey,
    [property: Id(2)] string State) : Synapse;

[GenerateSerializer]
[Alias("db.mcp.authorization-denied")]
public sealed record AuthorizationDenied(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string ServerKey,
    [property: Id(2)] string State) : Synapse;
