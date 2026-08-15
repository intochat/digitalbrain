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

