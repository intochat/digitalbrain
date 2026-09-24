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
