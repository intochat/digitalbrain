using System.Text;
using DigitalBrain.Apps;
using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Types;
using DigitalBrain.Flutter.Form;
using DigitalBrain.Flutter.Workspace;

namespace IntoChat.Apps;

// Save as app turns the open form and live-table windows of a workspace into a declarative app,
// and reopening the app replays exactly those windows.
internal sealed class WorkspaceAppService(IDigitalBrain brain)
{
    private const int MaxOpenRetries = 4;

    public async Task<AppInstallation> SaveAsync(string scope, string name, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var state = await brain.Get<IWorkspace>(scope).Read().WaitAsync(ct);
        var windows = state.Windows
            .Where(window => window.IsOpen && (window.Reference.Kind == WindowReference.TableKind || IsForm(window)))
            .ToArray();
        if (windows.Length == 0) { throw new InvalidOperationException("Open a form or a live table before saving an app."); }

        var slug = Slug(name);
        var request = new SaveAsAppRequest
        {
            Id = "intochat.saved-" + slug,
            Name = name,
            DescriptionForPeople = $"Saved from your workspace: {name}.",
            DescriptionForModel = $"Open the saved '{name}' app and its windows.",
            UiEntry = "app-saved-" + slug,
            Operations = await Operations(windows, ct),
            Windows =
            [
                .. windows.Select(window => new AppWindow
                {
                    WindowId = window.Id,
                    Title = window.Title,
                    Kind = window.Reference.Kind,
                    NeuronId = window.Reference.NeuronId,
                }),
            ],
        };
        return await brain.Get<IAppCatalog>(scope).SaveAsApp(request).WaitAsync(ct);
    }

    public async Task<WorkspaceState> OpenAsync(string scope, string appId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        var installation = await brain.Get<IAppCatalog>(scope).Read(appId).WaitAsync(ct)
            ?? throw new KeyNotFoundException($"App '{appId}' is not installed in this workspace.");
        var workspace = brain.Get<IWorkspace>(scope);
        foreach (var window in installation.Manifest.Windows)
        {
            await EnsureOpen(workspace, window, new(window.Kind, window.NeuronId), ct);
        }

        return await workspace.Read().WaitAsync(ct);
    }

    private async Task<IReadOnlyList<AppOperation>> Operations(IReadOnlyList<WorkspaceWindow> windows, CancellationToken ct)
    {
        var operations = new List<AppOperation>(windows.Count);
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var window in windows)
        {
            var name = Unique(Slug(window.Title), used);
            if (window.Reference.Kind == WindowReference.TableKind)
            {
                operations.Add(new()
                {
                    Name = name,
                    DescriptionForModel = $"Open the '{window.Title}' live table.",
                    ReadOnly = true,
                    OutputTypeId = "reference",
                });
                continue;
            }

            var form = await brain.Get<IForm>(window.Id).Read().WaitAsync(ct);
            operations.Add(new()
            {
                Name = name,
                DescriptionForModel = $"Fill in the '{window.Title}' form.",
                InputTypeIds = form.Fields
                    .Where(field => field.Kind != FieldKind.Reference)
                    .ToDictionary(field => field.Name, field => TypeCatalog.Get(field.Kind).Id, StringComparer.Ordinal),
                OutputTypeId = "reference",
            });
        }

        return operations;
    }

    private static async Task EnsureOpen(IWorkspace workspace, AppWindow window, WindowReference reference, CancellationToken ct)
    {
        for (var attempt = 0; attempt < MaxOpenRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var state = await workspace.Read().WaitAsync(ct);
            if (state.Windows.FirstOrDefault(existing => existing.Id == window.WindowId) is { IsOpen: true } open && open.Reference == reference)
            {
                return;
            }

            var operationId = Guid.NewGuid().ToString();
            try
            {
                if (reference.Kind == WindowReference.TableKind)
                {
                    await workspace.Open(new(operationId, window.WindowId, window.Title, reference, state.Revision)).WaitAsync(ct);
                }
                else
                {
                    await workspace.OpenSurface(new(operationId, window.WindowId, window.Title, reference, state.Revision)).WaitAsync(ct);
                }

                return;
            }
            catch (WorkspaceRevisionConflictException) when (attempt < MaxOpenRetries - 1) { }
        }

        throw new InvalidOperationException("The workspace changed too often; retry opening the app.");
    }

    private static bool IsForm(WorkspaceWindow window) =>
        window.Id.Contains("/apps/forms/", StringComparison.Ordinal);

    private static string Unique(string name, HashSet<string> used)
    {
        if (used.Add(name)) { return name; }
        for (var suffix = 2; ; suffix++)
        {
            var candidate = name + "-" + suffix;
            if (used.Add(candidate)) { return candidate; }
        }
    }

    private static string Slug(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character)) { builder.Append(character); }
            else if (builder.Length > 0 && builder[^1] != '-') { builder.Append('-'); }
        }

        var slug = builder.ToString().Trim('-');
        return slug.Length == 0 ? "app" : slug;
    }
}
