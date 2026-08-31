using System.Text.Json;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Integrations.Mcp;

public interface IMcpIntegrationClient
{
    Task<JsonElement> CallAsync(OwnerId owner, McpIntegrationEndpoint endpoint, string toolName,
        IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
        => CallAsync(endpoint, toolName, arguments, cancellationToken);

    Task<JsonElement> CallAsync(
        McpIntegrationEndpoint endpoint,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken);
}
