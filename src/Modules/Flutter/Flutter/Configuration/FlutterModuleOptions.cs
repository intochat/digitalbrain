using DigitalBrain.Core;

namespace DigitalBrain.Flutter;

public enum FlutterHostKind { Window, Headless, Web, None }
public sealed record FlutterHostingOptions
{
    public FlutterHostKind Kind { get; init; } = FlutterHostKind.Window;
}
public sealed record FlutterModuleOptions
{
    public FlutterHostingOptions Hosting { get; init; } = new();
}
