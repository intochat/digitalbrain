using System.Text.Json;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Modules.Sdk.Mcp;

[GenerateSerializer]
[Alias("db.mcp.tools-listed")]
public sealed record McpToolsListed(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] McpToolDescription[] Tools) : Synapse;

