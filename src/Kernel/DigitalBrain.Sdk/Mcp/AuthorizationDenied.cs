using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Modules.Sdk.Mcp;

[GenerateSerializer]
[Alias("db.mcp.authorization-denied")]
[Description("MCP authorization was denied")]
public sealed record AuthorizationDenied(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string ServerKey,
    [property: Id(2)] string State,
    [property: Id(3)] ActorContext? Actor = null) : Synapse;

