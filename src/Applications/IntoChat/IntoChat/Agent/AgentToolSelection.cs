using DigitalBrain.Apps;
using DigitalBrain.Contracts;
using DigitalBrain.Discovery;
using DigitalBrain.Flutter.Workspace;

namespace IntoChat.Agent;

// The app tools a turn gets beyond the definition's core: apps installed in the workspace, apps whose
// windows are open, and apps discovery matches to the owner's words (this message and the previous
// one, so a follow-up such as "Allow once" keeps the tools of the app it answers).
internal sealed class AgentToolSelection(IDigitalBrain brain)
{
    private static readonly Dictionary<string, string[]> AppTools = new(StringComparer.Ordinal)
    {
        ["intochat.leadgenerator"] = ["propose_app", "run_leadgenerator"],
        ["intochat.image-editor"] = ["plan_background_removal", "run_background_removal"],
    };

    public async Task<IReadOnlyList<string>> ResolveAsync(string scope, string message, IReadOnlyList<string> history, CancellationToken ct)
    {
        // History alternates user and assistant text, so the previous user message is second from the end.
        var query = history.Count >= 2 ? Own(message) + "\n" + Own(history[^2]) : Own(message);
        var reads = await Task.WhenAll(InstalledAsync(scope, ct), WindowsAsync(scope, ct), DiscoveredAsync(scope, query, ct));
        var appIds = reads.SelectMany(static ids => ids).Select(static id => id.Split('/', 2)[0]).ToHashSet(StringComparer.Ordinal);
        return [.. AppTools
            .Where(app => appIds.Any(id => id == app.Key || app.Key.EndsWith("." + id, StringComparison.Ordinal)))
            .SelectMany(static app => app.Value)
            .Distinct(StringComparer.Ordinal)];
    }

    // The chat client appends a hidden artifact-context paragraph to every message; intent comes from
    // the owner's own words before it.
    private static string Own(string text)
        => text.Split("\n\n[Conversation agent:", 2, StringSplitOptions.None)[0];

    private async Task<IReadOnlyList<string>> DiscoveredAsync(string scope, string message, CancellationToken ct)
    {
        try
        {
            var result = await brain.Get<ICapabilityCatalog>("catalog").SearchApps(message, scope, 5).WaitAsync(ct);
            return [.. result.Hits.Select(hit => hit.Id)];
        }
        catch
        {
            // Discovery is an optional module; without it the turn still gets the core tools.
            return [];
        }
    }

    private async Task<IReadOnlyList<string>> InstalledAsync(string scope, CancellationToken ct)
    {
        try
        {
            var installed = await brain.Get<IAppCatalog>(scope).List().WaitAsync(ct);
            return [.. installed.Select(installation => installation.Manifest.Id)];
        }
        catch
        {
            return [];
        }
    }

    private async Task<IReadOnlyList<string>> WindowsAsync(string scope, CancellationToken ct)
    {
        try
        {
            var state = await brain.Get<IWorkspace>(scope).Read().WaitAsync(ct);
            return [.. state.Windows
                .Select(window => AppSegment(window.Reference.NeuronId))
                .Where(segment => segment is { Length: > 0 })!];
        }
        catch
        {
            return [];
        }
    }

    private static string? AppSegment(string neuronId)
    {
        const string marker = "/apps/";
        var start = neuronId.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) { return null; }
        var rest = neuronId[(start + marker.Length)..];
        var end = rest.IndexOf('/');
        return end < 0 ? rest : rest[..end];
    }
}
