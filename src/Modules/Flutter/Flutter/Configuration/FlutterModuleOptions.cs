using DigitalBrain.Core;

namespace DigitalBrain.Flutter;

public enum FlutterHostKind { Window, Headless, Web, None }
public sealed record FlutterHostingOptions
{
    public FlutterHostKind Kind { get; init; } = FlutterHostKind.Window;
    public string ResourceName { get; init; } = "FlutterShell";
    public string DeviceTarget { get; init; } = "windows";
    public string ShellName { get; init; } = "desk";
    public string ChatName { get; init; } = "main";
    public string? FlutterCommand { get; init; }
    public string? DartCommand { get; init; }
    public string? WorkingDirectory { get; init; }
}
public sealed record FlutterModuleOptions
{
    public FlutterHostingOptions Hosting { get; set; } = new();
}
