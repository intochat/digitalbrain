using DigitalBrain.Apps;
using DigitalBrain.Contracts;
using DigitalBrain.Discovery;
using DigitalBrain.Flutter.Workspace;

namespace IntoChat.Agent;

// The app tools a turn gets beyond the definition's core, as each app's manifest declares them:
// apps installed in the workspace, apps whose windows are open, and apps discovery matches to the
// owner's words (this message and the previous one, so a follow-up such as "Allow once" keeps the
// tools of the app it answers). Every read is optional; a failed one only narrows the tools.
internal sealed class AgentToolSelection(IDigitalBrain brain)
{
    public async Task<IReadOnlyList<string>> ResolveAsync(string scope, string message, IReadOnlyList<string> history, CancellationToken ct)
    {
        // History alternates user and assistant text, so the previous user message is second from the end.
        var query = history.Count >= 2 ? Own(message) + "\n" + Own(history[^2]) : Own(message);
        var reads = await Task.WhenAll(InstalledAsync(scope, ct), WindowsAsync(scope, ct), DiscoveredAsync(scope, query, ct));
        return [.. reads.SelectMany(static tools => tools).Distinct(StringComparer.Ordinal)];
    }

    // The chat client appends a hidden artifact-context paragraph to every message; intent comes from
    // the owner's own words before it.
    private static string Own(string text)
        => text.Split("\n\n[Conversation agent:", 2, StringSplitOptions.None)[0];

    private async Task<IEnumerable<string>> DiscoveredAsync(string scope, string message, CancellationToken ct)
    {
        try
        {
            var result = await brain.Get<ICapabilityCatalog>("catalog").SearchApps(message, scope, 5).WaitAsync(ct);
            return result.Hits.SelectMany(static hit => hit.Tools);
        }
        catch
        {
            // Discovery is an optional module; without it the turn still gets the core tools.
            return [];
        }
    }

    private async Task<IEnumerable<string>> InstalledAsync(string scope, CancellationToken ct)
    {
        try
        {
            var installed = await brain.Get<IAppCatalog>(scope).List().WaitAsync(ct);
            return installed.SelectMany(static installation => installation.Manifest.AgentTools);
        }
        catch
        {
            return [];
        }
    }

    private async Task<IEnumerable<string>> WindowsAsync(string scope, CancellationToken ct)
    {
        try
        {
            var state = await brain.Get<IWorkspace>(scope).Read().WaitAsync(ct);
            var segments = state.Windows.Select(window => AppSegment(window.Reference.NeuronId)).OfType<string>().ToHashSet(StringComparer.Ordinal);
            if (segments.Count == 0) { return []; }
            // A window names its app by the last segment of the app id, such as "leadgenerator".
            var manifests = await brain.Get<IAppManifestDirectory>(AppManifestDirectoryGrains.Key).Read().WaitAsync(ct);
            return manifests
                .Where(scoped => segments.Contains(scoped.Manifest.Id) || segments.Contains(scoped.Manifest.Id.Split('.')[^1]))
                .SelectMany(static scoped => scoped.Manifest.AgentTools);
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
        var segment = end < 0 ? rest : rest[..end];
        return segment.Length > 0 ? segment : null;
    }
}
