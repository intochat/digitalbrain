using Microsoft.Extensions.Logging;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Kernel.Enforcement;
using System.Runtime.CompilerServices;
using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Kernel;
using DigitalBrain.AI.Metering;
using Microsoft.Extensions.Options;
using Orleans.Runtime;

namespace DigitalBrain.AI.Agents;

[GrainType("ai.agent-execution")]
internal sealed class AgentExecutionNeuron(IAgentTurnRunner runner, IIntentUsageSink usage,
    ModelProfiles profiles, IOptionsMonitor<AIOptions> options, ILogger<AgentExecutionNeuron> logger) : Neuron, IAgentExecution
{
    public Task<ModelCatalog> Models(string? defaultModel = null)
        => Task.FromResult(new AgentModelCatalog(profiles, options, defaultModel).Read());
    public Task<AgentModelSelection?> SelectModel(string? id, string? defaultModel = null)
        => Task.FromResult(new AgentModelCatalog(profiles, options, defaultModel).Select(id));

    public async IAsyncEnumerable<AgentTurnEvent> Run(AgentTurnRequest request, string intentId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (CallerContextStamper.TryGet(out var caller) && caller.Kind != CallerKind.Platform
            && request.ScopeId != BrainScope.CurrentId())
        { throw new UnauthorizedAccessException("Agent execution belongs to another brain."); }
        using var intent = IntentContext.Begin(intentId, request.ScopeId);
        try
        {
            await foreach (var item in runner.RunAsync(request, ct)) { yield return item; }
        }
        finally
        {
            try { await usage.FlushAsync(intent, CancellationToken.None); }
            catch (Exception error)
            { logger.LogWarning(error, "Agent usage flush failed for intent {IntentId}", intentId); }
        }
    }
}
