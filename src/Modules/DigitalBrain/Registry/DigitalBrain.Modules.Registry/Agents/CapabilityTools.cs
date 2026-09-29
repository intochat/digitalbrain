using System.ComponentModel;
using System.Text;
using DigitalBrain.AI.Agents;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Registry.Agents;

// Before a turn the model gets the neuron methods that match the message as tools; find_capability
// searches again and offers what it finds to the rest of the turn.
public sealed class CapabilityTools(IGrainFactory grains) : IAgentToolFactory, IAgentContextProvider
{
    public const string ProviderName = "capabilities";
    public const string FindTool = "find_capability";
    private const string CatalogKey = "catalog";
    private const int Take = 8;

    public string Name => ProviderName;

    public async Task<AgentContext> Provide(AgentContextRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Message)) { return AgentContext.None; }
        var hits = await Search(request.Message, request.ScopeId, ct);
        if (hits.Count == 0) { return AgentContext.None; }
        var text = new StringBuilder("Capabilities matching this request (call them as tools; use find_capability for others):\n");
        foreach (var hit in hits) { text.Append("- ").Append(hit.Tool ?? hit.Id).Append(": ").Append(hit.Name).Append('\n'); }
        return new(text.ToString(), [.. hits.SelectMany(static hit => hit.Tools)]);
    }

    public IReadOnlyList<AIFunction> Create(Func<AgentToolContext> context)
    {
        async Task<AgentToolOffer> Find([Description("What you need to do, in plain words.")] string query, CancellationToken ct)
        {
            var hits = await Search(query, context().ScopeId, ct);
            return new([.. hits.SelectMany(static hit => hit.Tools)], hits.Count == 0
                ? "No capability matched. Do not invent one; tell the user what is missing."
                : hits.Select(static hit => new { tool = hit.Tool, id = hit.Id, name = hit.Name, description = hit.Description }).ToArray());
        }

        return [AIFunctionFactory.Create(Find, new AIFunctionFactoryOptions
        {
            Name = FindTool,
            Description = "Search every capability (neuron methods, apps) by what you need to do. Matching methods become callable tools.",
            MarshalResult = static (result, _, _) => new ValueTask<object?>(result),
        })];
    }

    private async Task<IReadOnlyList<Found>> Search(string query, string scopeId, CancellationToken ct)
    {
        var result = await grains.GetGrain<ICapabilityCatalog>(CatalogKey).Search(query, scopeId, Take).WaitAsync(ct);
        return [.. result.Hits.Select(static hit =>
        {
            var tool = hit.Kind == CapabilityKind.Neuron ? NeuronToolName.Of(hit.Id) : null;
            return new Found(hit.Id, hit.Name, hit.Description, tool, tool is null ? hit.Tools : [tool, .. hit.Tools]);
        })];
    }

    private sealed record Found(string Id, string Name, string Description, string? Tool, IReadOnlyList<string> Tools);
}