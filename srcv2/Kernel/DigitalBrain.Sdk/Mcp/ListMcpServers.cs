using System.Text.Json;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Modules.Sdk.Mcp;

[GenerateSerializer]
[Alias("db.mcp.list-servers")]
public sealed record ListMcpServers(
    [property: Id(0)] CommandId CommandId) : RequestSynapse<McpServersListed>;

