namespace DigitalBrain.Apps;

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
