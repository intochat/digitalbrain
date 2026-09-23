using DigitalBrain.Contracts;
using Orleans.Metadata;

namespace DigitalBrain.Apps;

// One installed, immutable version of an app in a workspace. Installing the same version twice is
// idempotent; a newer version is appended, never rewritten.
[GenerateSerializer, Alias("apps.installation")]
public sealed record AppInstallation
{
    [Id(0)] public required AppManifest Manifest { get; init; }
    [Id(1)] public required DateTimeOffset InstalledAt { get; init; }
    [Id(2)] public bool Active { get; init; } = true;
}

// Uninstall reports exactly which app data is kept: the app's own workspace data survives, while the
// installation record and its UI entry are removed. My Data is never owned by an app.
[GenerateSerializer, Alias("apps.uninstall-outcome")]
public sealed record AppUninstallOutcome
{
    [Id(0)] public required string AppId { get; init; }
    [Id(1)] public IReadOnlyList<string> KeptData { get; init; } = [];
    [Id(2)] public IReadOnlyList<string> RemovedData { get; init; } = [];
}

// A Save as app request turns the current window into a declarative app in this workspace.
[GenerateSerializer, Alias("apps.save-as-app")]
public sealed record SaveAsAppRequest
{
    [Id(0)] public required string Id { get; init; }
    [Id(1)] public required string Name { get; init; }
    [Id(2)] public required string DescriptionForPeople { get; init; }
    [Id(3)] public string? DescriptionForModel { get; init; }
    [Id(4)] public string? UiEntry { get; init; }
    [Id(5)] public IReadOnlyList<AppOperation> Operations { get; init; } = [];
    [Id(6)] public IReadOnlyList<string> ExamplePrompts { get; init; } = [];
    [Id(7)] public IReadOnlyList<AppPermission> Permissions { get; init; } = [];
    [Id(8)] public IReadOnlyList<AppMeter> Meters { get; init; } = [];
    [Id(9)] public IReadOnlyList<AppScenario> Scenarios { get; init; } = [];
    [Id(10)] public IReadOnlyList<AppWindow> Windows { get; init; } = [];
}

// Where an app's manifest came from: committed first-party/platform manifests are global and visible
// to every workspace, while a saved or workspace-installed app belongs to the workspace that owns it.
public enum AppManifestScope
{
    Global = 0,
    Workspace = 1,
}

[GenerateSerializer, Alias("apps.scoped-manifest")]
public sealed record ScopedAppManifest
{
    [Id(0)] public required AppManifest Manifest { get; init; }
    [Id(1)] public AppManifestScope Scope { get; init; }
    [Id(2)] public string? WorkspaceId { get; init; }

    public static ScopedAppManifest Global(AppManifest manifest) =>
        new() { Manifest = manifest, Scope = AppManifestScope.Global };

    public static ScopedAppManifest InWorkspace(AppManifest manifest, string workspaceId) =>
        new() { Manifest = manifest, Scope = AppManifestScope.Workspace, WorkspaceId = workspaceId };

    public string? OwningWorkspaceId =>
        Scope == AppManifestScope.Workspace ? WorkspaceId : null;
}

// The app directory makes saved and installed apps discoverable: the Discovery module reads it as its
// manifest source. It always exposes the committed first-party manifests as global entries, and it
// records the owning workspace on every saved or installed manifest so discovery can scope search.
[Alias("app-manifest-directory"), DefaultGrainType("app-manifest-directory")]
public interface IAppManifestDirectory : INeuron
{
    Task<AppManifest> Publish(AppManifest manifest, string workspaceId);

    Task<IReadOnlyList<ScopedAppManifest>> Read();
}

public static class AppManifestDirectoryGrains
{
    public const string Key = "apps";
}

// The per-workspace app catalog. Key the grain by the workspace scope id.
[Alias("app-catalog"), DefaultGrainType("app-catalog")]
public interface IAppCatalog : INeuron
{
    Task<AppInstallation> Install(AppManifest manifest);

    Task<AppInstallation> Rollback(string appId);

    Task<AppUninstallOutcome> Uninstall(string appId);

    Task<IReadOnlyList<AppInstallation>> List();

    Task<AppInstallation?> Read(string appId);

    Task<AppInstallation> SaveAsApp(SaveAsAppRequest request);
}
