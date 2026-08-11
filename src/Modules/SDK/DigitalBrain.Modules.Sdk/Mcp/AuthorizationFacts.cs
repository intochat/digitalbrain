using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Modules.Sdk.Mcp;

[GenerateSerializer]
[Alias("db.mcp.authorization-required")]
[Description("MCP server requires interactive authorization")]
public sealed record AuthorizationRequired(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string ServerKey,
    [property: Id(2)] string ServerDisplayName,
    [property: Id(3)] Uri SignInUrl,
    [property: Id(4)] string State,
    [property: Id(5)] ActorContext? Actor = null) : Synapse;

[GenerateSerializer]
[Alias("db.mcp.authorization-completed")]
[Description("MCP authorization completed")]
public sealed record AuthorizationCompleted(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string ServerKey,
    [property: Id(2)] string State,
    [property: Id(3)] ActorContext? Actor = null) : Synapse;

[GenerateSerializer]
[Alias("db.mcp.authorization-denied")]
[Description("MCP authorization was denied")]
public sealed record AuthorizationDenied(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string ServerKey,
    [property: Id(2)] string State,
    [property: Id(3)] ActorContext? Actor = null) : Synapse;
