using System.ComponentModel;
using DigitalBrain.AI.Agents;
using DigitalBrain.Contracts;
using DigitalBrain.Discovery;
using Microsoft.Extensions.AI;

namespace IntoChat.Agent;

// Exposes the manifest catalog to the turn. Search returns ids only, so the model always
// reads a capability through its manifest rather than inventing one.
internal sealed class DiscoveryTools(IDigitalBrain brain) : IAgentToolFactory
{
    private const string CatalogKey = "catalog";

    public IReadOnlyList<AIFunction> Create(Func<AgentToolContext> context)
    {
        async Task<object> FindCapability(
            [Description("What the user wants to do, in their own words.")] string query,
            CancellationToken ct)
        {
            var trusted = context();
            try
            {
                var catalog = brain.Get<ICapabilityCatalog>(CatalogKey);
                var result = await catalog.Search(query, trusted.ScopeId, 5).WaitAsync(ct);
                var capabilities = await Task.WhenAll(result.Hits.Select(async hit => new
                {
                    id = hit.Id,
                    kind = hit.Kind.ToString(),
                    score = hit.Score,
                    neuron = hit.Kind == CapabilityKind.Neuron
                        ? await catalog.ReadNeuron(hit.Id).WaitAsync(ct)
                        : null,
                }));
                return new
                {
                    degraded = result.Degraded,
                    capabilities,
                    guidance = result.Hits.Count == 0
                        ? "No capability matched. Do not invent one; name what is missing and continue with the capabilities you have."
                        : "App ids must be read through their manifests. Neuron entries include registry metadata; they do not grant a callable tool.",
                };
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException)
            {
                return new { isError = true, message = error.Message };
            }
        }

        return
        [
            AIFunctionFactory.Create(
                FindCapability,
                "find_capability",
                "Find installed apps, operations, and routable neuron contracts by what the user wants to do."),
        ];
    }
}
