using System.Text.Json;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Modules.Sdk.Mcp;

[GenerateSerializer]
[Alias("db.mcp.call-tool")]
public sealed record CallMcpTool(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string Tool,
    [property: Id(2)] JsonElement Arguments,
    [property: Id(3)] string? FireRowsAs = null,
    [property: Id(4)] ActorContext? Actor = null,
    // Destructive tools require an explicit second fire with this set (S18 one-shot press).
    [property: Id(5)] bool ConfirmDestructive = false) : RequestSynapse<McpToolReturned>;

