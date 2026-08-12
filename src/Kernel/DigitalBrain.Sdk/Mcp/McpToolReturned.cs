using System.Text.Json;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Modules.Sdk.Mcp;

[GenerateSerializer]
[Alias("db.mcp.tool-returned")]
public sealed record McpToolReturned(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string Tool,
    [property: Id(2)] JsonElement Content,
    [property: Id(3)] int FiredRows,
    [property: Id(4)] ActorContext? Actor = null,
    [property: Id(5)] string? IntegrationSubject = null,
    // Whole-batch FireRowsAs summary (S20): truth about the cap, not a pager.
    [property: Id(6)] bool Truncated = false,
    [property: Id(7)] int RowsAvailable = 0,
    [property: Id(8)] string? Summary = null) : Synapse;

