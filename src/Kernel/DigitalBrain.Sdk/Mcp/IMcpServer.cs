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
    IHandle<CallMcpTool>,
    IHandle<ListMcpServers>;

[GenerateSerializer]
[Alias("db.mcp.list-tools")]
public sealed record ListMcpTools(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] ActorContext? Actor = null) : RequestSynapse<McpToolsListed>;

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
    [property: Id(3)] string? FireRowsAs = null,
    [property: Id(4)] ActorContext? Actor = null,
    // Destructive tools require an explicit second fire with this set (S18 one-shot press).
    [property: Id(5)] bool ConfirmDestructive = false) : RequestSynapse<McpToolReturned>;

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

[GenerateSerializer]
[Alias("db.mcp.list-servers")]
public sealed record ListMcpServers(
    [property: Id(0)] CommandId CommandId) : RequestSynapse<McpServersListed>;

[GenerateSerializer]
[Alias("db.mcp.servers-listed")]
public sealed record McpServersListed(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] McpServerInfo[] Servers) : Synapse;

[GenerateSerializer]
[Alias("db.mcp.server-info")]
public sealed record McpServerInfo(
    [property: Id(0)] string Key,
    [property: Id(1)] string DisplayName,
    [property: Id(2)] string Endpoint,
    [property: Id(3)] string[] Scopes);

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
