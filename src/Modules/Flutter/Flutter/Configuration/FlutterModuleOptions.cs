using DigitalBrain.Core;

namespace DigitalBrain.Flutter;

// Preserve serialized values; 1 belonged to the removed Dart console host.
public enum FlutterHostKind { Window = 0, Web = 2, None = 3 }
public sealed record FlutterHostingOptions
{
    public FlutterHostKind Kind { get; init; } = FlutterHostKind.Window;
    public string ResourceName { get; init; } = "FlutterShell";
    public string DeviceTarget { get; init; } = "windows";
    public string ShellName { get; init; } = "desk";
    public string ChatName { get; init; } = "main";
    public string? FlutterCommand { get; init; }
    public string? WorkingDirectory { get; init; }
}
public sealed record FlutterModuleOptions
{
    public FlutterHostingOptions Hosting { get; set; } = new();
}
