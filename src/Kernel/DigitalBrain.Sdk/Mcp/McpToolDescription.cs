using System.Text.Json;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Modules.Sdk.Mcp;

[GenerateSerializer]
[Alias("db.mcp.tool-description")]
public sealed record McpToolDescription(
    [property: Id(0)] string Name,
    [property: Id(1)] string Description,
    [property: Id(2)] string InputSchemaJson,
    [property: Id(3)] bool Destructive);

