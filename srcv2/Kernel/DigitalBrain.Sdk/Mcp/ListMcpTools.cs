using System.Text.Json;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Modules.Sdk.Mcp;

[GenerateSerializer]
[Alias("db.mcp.list-tools")]
public sealed record ListMcpTools(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] ActorContext? Actor = null) : RequestSynapse<McpToolsListed>;

