namespace DigitalBrain.Flutter.Aspire.Hosting;

public sealed class FlutterHostOptions
{
    public const string SectionName = "DigitalBrain:Flutter:Hosting";

    public FlutterHostKind Kind { get; set; } = FlutterHostKind.Window;

    public string ResourceName { get; set; } = ShellNames.DefaultFlutterResourceName;

    public string DeviceTarget { get; set; } = ShellNames.DefaultDeviceTarget;

    public string ShellName { get; set; } = ShellNames.DefaultShellName;

    public string ChatName { get; set; } = ShellNames.DefaultChatName;

    public string? FlutterCommand { get; set; }

    public string? WorkingDirectory { get; set; }

    public bool ReleaseBuild { get; set; }

    // Web only: serve the prebuilt release bundle from a docker image (shell/Dockerfile)
    // instead of compiling per AppHost with flutter run. Hosting-layer knob, deliberately
    // absent from FlutterHostingOptions: it describes the machine running the AppHost, not
    // the product composition, so CI flips it for every host via configuration.
    public bool WebContainer { get; set; }
}
