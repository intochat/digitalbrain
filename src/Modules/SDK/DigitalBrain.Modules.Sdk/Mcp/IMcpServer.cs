using System.Text.Json;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Modules.Sdk.Mcp;

// One neuron per configured MCP server (instance name = server key). The server's
// own tool catalog IS the capability surface; nothing here enumerates actions.
[ClientEntryPoint]
[Alias("mcp")]
public partial interface IMcp :
    INeuron,
    IHandle<ListMcpTools>,
    IHandle<CallMcpTool>;

[GenerateSerializer]
[Alias("db.mcp.list-tools")]
public sealed record ListMcpTools(
    [property: Id(0)] CommandId CommandId) : RequestSynapse<McpToolsListed>;

[GenerateSerializer]
[Alias("db.mcp.tools-listed")]
public sealed record McpToolsListed(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] McpToolDescription[] Tools) : Synapse;

[GenerateSerializer]
[Alias("db.mcp.tool-description")]
public sealed record McpToolDescription(
    [property: Id(0)] string Name,
    [property: Id(1)] string Description,
    [property: Id(2)] string InputSchemaJson,
    [property: Id(3)] bool Destructive);

[GenerateSerializer]
[Alias("db.mcp.call-tool")]
public sealed record CallMcpTool(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string Tool,
    [property: Id(2)] JsonElement Arguments,
    [property: Id(3)] string? FireRowsAs = null) : RequestSynapse<McpToolReturned>;

[GenerateSerializer]
[Alias("db.mcp.tool-returned")]
public sealed record McpToolReturned(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string Tool,
    [property: Id(2)] JsonElement Content,
    [property: Id(3)] int FiredRows) : Synapse;

public interface IMcpToolTransport
{
    Task<IReadOnlyList<McpToolDescription>> ListToolsAsync(
        McpServerDefinition server,
        CancellationToken cancellationToken);

    Task<JsonElement> CallToolAsync(
        McpServerDefinition server,
        string tool,
        JsonElement arguments,
        CancellationToken cancellationToken);
}
