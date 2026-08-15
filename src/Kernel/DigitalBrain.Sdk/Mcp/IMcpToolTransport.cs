using System.Text.Json;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Modules.Sdk.Mcp;

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

