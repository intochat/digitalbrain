using System.Text.Json;

namespace DigitalBrain.Integrations.Mcp;

public interface IMcpIntegrationClient
{
    Task<JsonElement> CallAsync(
        McpIntegrationEndpoint endpoint,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken);
}
