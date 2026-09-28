using System.Text;
using DigitalBrain.AI.Agents;
using DigitalBrain.Contracts;
using DigitalBrain.Discovery;

namespace IntoChat.Agent;

// Before every turn the model sees the capabilities that match the message, so routing does not
// depend on it remembering to call find_capability first, and gets the tools those capabilities declare.
internal sealed class CapabilityContextProvider(IDigitalBrain brain) : IAgentContextProvider
{
    public const string ProviderName = "capabilities";
    private const string CatalogKey = "catalog";
    private const int Take = 5;
    private const int DescriptionLimit = 200;

    public string Name => ProviderName;

    public async Task<AgentContext> Provide(AgentContextRequest request, CancellationToken ct)
    {
        var message = Own(request.Message);
        if (string.IsNullOrWhiteSpace(message)) { return AgentContext.None; }
        // The previous message keeps a follow-up such as "Allow once" tied to the app it answers.
        var query = request.PreviousMessage is { Length: > 0 } previous ? message + "\n" + Own(previous) : message;
        var result = await brain.Get<ICapabilityCatalog>(CatalogKey).Search(query, request.ScopeId, Take).WaitAsync(ct);
        if (result.Hits.Count == 0) { return AgentContext.None; }
        var context = new StringBuilder("## Available capabilities for this request\n");
        foreach (var hit in result.Hits)
        {
            var description = hit.Description.Length > DescriptionLimit ? hit.Description[..DescriptionLimit] + "…" : hit.Description;
            context.Append("- ").Append(hit.Id).Append(" (").Append(hit.Kind).Append("): ").Append(hit.Name);
            if (description.Length > 0) { context.Append(" — ").Append(description); }
            context.Append('\n');
        }
        context.Append("Use find_capability to search further. App ids are read through their manifests.");
        return new(context.ToString(), [.. result.Hits.SelectMany(static hit => hit.Tools).Distinct(StringComparer.Ordinal)]);
    }

    // The chat client appends a hidden artifact-context paragraph to every message; intent is the
    // owner's own words before it.
    private static string Own(string text) => text.Split("\n\n[Conversation agent:", 2, StringSplitOptions.None)[0];
}
