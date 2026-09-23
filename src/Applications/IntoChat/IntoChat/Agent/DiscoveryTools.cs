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
                var result = await brain.Get<ICapabilityCatalog>(CatalogKey).Search(query, trusted.ScopeId, 5).WaitAsync(ct);
                return new
                {
                    degraded = result.Degraded,
                    capabilities = result.Hits.Select(hit => new { id = hit.Id, kind = hit.Kind.ToString(), score = hit.Score }).ToArray(),
                    guidance = result.Hits.Count == 0
                        ? "No capability matched. Do not invent one; name what is missing and continue with the capabilities you have."
                        : "Choose one of these ids and read its manifest; the manifest is the truth.",
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
                "Find installed apps and operations by what the user wants to do. Returns capability ids only."),
        ];
    }
}
