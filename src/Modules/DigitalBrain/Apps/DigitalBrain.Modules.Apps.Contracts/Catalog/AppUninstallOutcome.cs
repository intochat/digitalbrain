namespace DigitalBrain.Apps;

// Uninstall reports exactly which app data is kept: the app's own workspace data survives, while the
// installation record and its UI entry are removed. Shared secrets are never owned by an app.
[GenerateSerializer, Alias("apps.uninstall-outcome")]
public sealed record AppUninstallOutcome
{
    [Id(0)] public required string AppId { get; init; }
    [Id(1)] public IReadOnlyList<string> KeptData { get; init; } = [];
    [Id(2)] public IReadOnlyList<string> RemovedData { get; init; } = [];
}
