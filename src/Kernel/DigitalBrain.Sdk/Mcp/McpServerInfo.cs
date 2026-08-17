using System.Text.Json;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Modules.Sdk.Mcp;

[GenerateSerializer]
[Alias("db.mcp.server-info")]
public sealed record McpServerInfo(
    [property: Id(0)] string Key,
    [property: Id(1)] string DisplayName,
    [property: Id(2)] string Endpoint,
    [property: Id(3)] string[] Scopes);

