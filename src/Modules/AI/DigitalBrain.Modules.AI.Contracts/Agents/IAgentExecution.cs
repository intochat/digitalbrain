using DigitalBrain;
using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.AI.Agents;

[Alias("ai.agent-execution"), Orleans.Metadata.DefaultGrainType("ai.agent-execution")]
public interface IAgentExecution : INeuron
{
    [AlwaysInterleave, ResponseTimeout("00:10:00")]
    IAsyncEnumerable<AgentTurnEvent> Run(AgentTurnRequest request, string intentId, CancellationToken ct = default);
    Task<ModelCatalog> Models(string? defaultModel = null);
    Task<AgentModelSelection?> SelectModel(string? id, string? defaultModel = null);
}
