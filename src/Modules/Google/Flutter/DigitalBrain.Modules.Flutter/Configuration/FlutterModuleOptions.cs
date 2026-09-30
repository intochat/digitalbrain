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
    public bool ReleaseBuild { get; init; }
}
public sealed record FlutterModuleOptions : IModuleOptions
{
    public FlutterHostingOptions Hosting { get; set; } = new();

    public FlutterModuleOptions RunWebApp() => WithHost(FlutterHostKind.Web);
    public FlutterModuleOptions RunDesktopApp() => WithHost(FlutterHostKind.Window);
    public FlutterModuleOptions BackendOnly() => WithHost(FlutterHostKind.None);
    public FlutterModuleOptions AsReleaseBuild() { Hosting = Hosting with { ReleaseBuild = true }; return this; }
    public FlutterModuleOptions AsDebugBuild() { Hosting = Hosting with { ReleaseBuild = false }; return this; }

    public void Validate()
    {
        if (!Enum.IsDefined(Hosting.Kind)) { throw new ArgumentOutOfRangeException(nameof(Hosting), "Unknown Flutter host kind."); }
        if (string.IsNullOrWhiteSpace(Hosting.ResourceName) || string.IsNullOrWhiteSpace(Hosting.ShellName)
            || string.IsNullOrWhiteSpace(Hosting.ChatName))
        { throw new ArgumentException("Flutter resource, shell and chat names must be specified."); }
    }

    private FlutterModuleOptions WithHost(FlutterHostKind kind) { Hosting = Hosting with { Kind = kind }; return this; }
}