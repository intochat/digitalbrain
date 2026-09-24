using DigitalBrain.Apps;
using DigitalBrain.Contracts;
using DigitalBrain.Discovery;
using DigitalBrain.Flutter.Workspace;

namespace IntoChat.Agent;

// The tools a turn gets beyond the always-on core: the apps discovery matches, the apps installed in
// the workspace, and the app a window belongs to. A data-shaped request also keeps the generic
// live-table pair so a plain "show me the customers" request never loses the table path.
//
// Discovery, the installed-app list and the workspace snapshot are only read when the owner's own
// words and the open windows have not already determined the tools, so a plain table turn makes no
// selection round-trips at all.
internal sealed class AgentToolSelection(IDigitalBrain brain)
{
    private static readonly Dictionary<string, string[]> AppTools = new(StringComparer.Ordinal)
    {
        ["intochat.leadgenerator"] = ["propose_app", "run_leadgenerator"],
        ["intochat.image-editor"] = ["plan_background_removal", "run_background_removal"],
    };

    private static readonly string[] TableKeywords =
        ["table", "supabase", "database", "query", "rows", "how many", "leads", "customer", "count", "sql", "select"];

    // A turn only needs the workspace snapshot when it may touch an app window.
    private static readonly string[] WindowKeywords =
        ["app", "open", "window", "form", "view", "tab", "install"];

    private static readonly Dictionary<string, string[]> KeywordApps = new(StringComparer.Ordinal)
    {
        ["intochat.leadgenerator"] = ["dental", "clinic", "find new", "lead", "approve"],
        ["intochat.image-editor"] = ["background", "photo", "image", "allow", "approval"],
    };

    public async Task<ToolSelection> ResolveAsync(string scope, string message, IReadOnlyList<string> history, CancellationToken ct)
    {
        var own = Own(message);
        // The owner's own words make an app relevant: a follow-up "Allow once" keeps the tools of the
        // app proposed in the previous turn. The client's hidden artifact suffix is not intent.
        var context = history.Count == 0 ? own : own + "\n" + string.Join("\n", history.Select(Own));
        var tableIntent = TableIntent(context);

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (appId, keywords) in KeywordApps)
        {
            if (keywords.Any(keyword => context.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            { ids.Add(appId); }
        }

        // A plain data request is served by the generic live-table pair, so the installed apps and
        // discovery cannot add anything: skip both. Any other request reads them, which is where an
        // app the owner did not name can surface.
        if (ids.Count == 0 && !tableIntent)
        {
            foreach (var id in await InstalledAsync(scope, ct)) { ids.Add(id); }
            foreach (var id in await DiscoveredAsync(scope, own, ct)) { ids.Add(id); }
        }

        // The workspace snapshot is only needed when the owner's words point at an app window; the
        // apps its open windows belong to are the only thing it adds. A turn
        // about a table or a plain question skips the read.
        var windows = NeedsWindows(context) ? await WindowsAsync(scope, ct) : [];
        foreach (var id in windows) { ids.Add(id); }

        var tools = new List<string>();
        foreach (var id in ids)
        {
            foreach (var appId in AppTools.Keys)
            {
                if (string.Equals(appId, id, StringComparison.Ordinal) || appId.EndsWith("." + id, StringComparison.Ordinal))
                { tools.AddRange(AppTools[appId]); }
            }
        }
        return new ToolSelection([.. tools.Distinct(StringComparer.Ordinal)], tableIntent);
    }

    public static bool TableIntent(string message) =>
        !string.IsNullOrWhiteSpace(message)
        && TableKeywords.Any(keyword => message.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    internal static bool NeedsWindows(string context) =>
        WindowKeywords.Any(keyword => context.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    // The chat client appends a hidden artifact-context paragraph to every message; intent comes from
    // the owner's own words before it.
    private static string Own(string text)
        => text.Split("\n\n[Conversation agent:", 2, StringSplitOptions.None)[0];

    private async Task<IReadOnlyList<string>> DiscoveredAsync(string scope, string message, CancellationToken ct)
    {
        try
        {
            var result = await brain.Get<ICapabilityCatalog>("catalog").Search(message, scope, 5).WaitAsync(ct);
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

internal sealed record ToolSelection(IReadOnlyList<string> AppTools, bool TableIntent);