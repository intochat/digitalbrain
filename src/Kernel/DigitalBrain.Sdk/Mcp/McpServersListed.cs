using System.Text.Json;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Modules.Sdk.Mcp;

[GenerateSerializer]
[Alias("db.mcp.servers-listed")]
public sealed record McpServersListed(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] McpServerInfo[] Servers) : Synapse;

