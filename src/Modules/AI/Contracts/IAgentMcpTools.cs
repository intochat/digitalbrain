using DigitalBrain.Abstractions.Identity;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

public interface IAgentMcpTools
{
    string Name { get; }

    Task<IReadOnlyList<AIFunction>> GetToolsAsync(NeuronId agent, CancellationToken cancellationToken);

    Task InvalidateAsync(NeuronId agent, CancellationToken cancellationToken = default);
}
