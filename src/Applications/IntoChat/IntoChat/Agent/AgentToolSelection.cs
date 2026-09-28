using DigitalBrain.Apps;
using DigitalBrain.Contracts;
using DigitalBrain.Flutter.Workspace;

namespace IntoChat.Agent;

// The app tools a turn gets from the workspace itself, as each app's manifest declares them: apps
// installed in it and apps whose windows are open. Tools of apps that match the owner's words come
// from the capability context. Every read is optional; a failed one only narrows the tools.
internal sealed class AgentToolSelection(IDigitalBrain brain)
{
    public async Task<IReadOnlyList<string>> ResolveAsync(string scope, CancellationToken ct)
    {
        var reads = await Task.WhenAll(InstalledAsync(scope, ct), WindowsAsync(scope, ct));
        return [.. reads.SelectMany(static tools => tools).Distinct(StringComparer.Ordinal)];
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
