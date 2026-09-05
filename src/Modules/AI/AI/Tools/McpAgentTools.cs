using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Sdk;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace DigitalBrain.AI;

public class McpAgentTools(
    McpStdioConnection connection,
    Func<McpClient, NeuronId, CancellationToken, Task>? configureSession = null) : IAgentMcpTools, IAsyncDisposable
{
    private readonly McpDiscoveredToolClient<NeuronId> _client = new(connection, configureSession);

    public string Name => connection.Name;

    public Task<IReadOnlyList<AIFunction>> GetToolsAsync(NeuronId agent, CancellationToken cancellationToken)
        => _client.GetToolsAsync(agent, cancellationToken);

    public Task InvalidateAsync(NeuronId agent, CancellationToken cancellationToken = default)
        => _client.InvalidateAsync(agent, cancellationToken);

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
