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
    [Id(7)] public IReadOnlyList<AppWindow> Windows { get; init; } = [];
}

// The global app directory makes saved and installed apps discoverable: the Discovery module reads
// it as its manifest source, so cataloging an app also makes it searchable by its description.
[Alias("app-manifest-directory"), DefaultGrainType("app-manifest-directory")]
public interface IAppManifestDirectory : INeuron
{
    Task<AppManifest> Publish(AppManifest manifest);

    Task<IReadOnlyList<AppManifest>> Read();
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
