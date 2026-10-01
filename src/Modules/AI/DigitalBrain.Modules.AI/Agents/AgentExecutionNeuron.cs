using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core.Enforcement;
using System.Runtime.CompilerServices;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.AI.Metering;
using Microsoft.Extensions.Options;
using Orleans.Runtime;

namespace DigitalBrain.AI.Agents;

[GrainType("ai.agent-execution")]
internal sealed class AgentExecutionNeuron(IAgentTurnRunner runner, IIntentUsageSink usage,
    ModelProfiles profiles, IOptionsMonitor<AIOptions> options) : Neuron, IAgentExecution
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
        finally { await usage.FlushAsync(intent, CancellationToken.None); }
    }
}
