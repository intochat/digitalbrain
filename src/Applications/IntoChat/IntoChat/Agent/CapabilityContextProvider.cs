using System.Text;
using DigitalBrain.AI.Agents;
using DigitalBrain.Contracts;
using DigitalBrain.Discovery;

namespace IntoChat.Agent;

// Before every turn the model sees the capabilities that match the message, so routing does not
// depend on it remembering to call find_capability first.
internal sealed class CapabilityContextProvider(IDigitalBrain brain) : IAgentContextProvider
{
    public const string ProviderName = "capabilities";
    private const string CatalogKey = "catalog";
    private const int Take = 5;
    private const int DescriptionLimit = 200;

    public string Name => ProviderName;

    public async Task<string?> Provide(AgentContextRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Message)) { return null; }
        var result = await brain.Get<ICapabilityCatalog>(CatalogKey).Search(request.Message, request.ScopeId, Take).WaitAsync(ct);
        if (result.Hits.Count == 0) { return null; }
        var context = new StringBuilder("## Available capabilities for this request\n");
        foreach (var hit in result.Hits)
        {
            var description = hit.Description.Length > DescriptionLimit ? hit.Description[..DescriptionLimit] + "…" : hit.Description;
            context.Append("- ").Append(hit.Id).Append(" (").Append(hit.Kind).Append("): ").Append(hit.Name);
            if (description.Length > 0) { context.Append(" — ").Append(description); }
            context.Append('\n');
        }
        return context.Append("Use find_capability to search further. App ids are read through their manifests.").ToString();
    }
}
