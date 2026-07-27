namespace DigitalBrain.Flutter.Aspire.Hosting;

public sealed class FlutterHostOptions
{
    public string ResourceName { get; set; } = FlutterHostingExtensions.DefaultFlutterResourceName;

    public string DeviceTarget { get; set; } = FlutterHostingExtensions.DefaultDeviceTarget;

    public string ShellName { get; set; } = FlutterHostingExtensions.DefaultShellName;

    public string ChatName { get; set; } = FlutterHostingExtensions.DefaultChatName;

    public string? FlutterCommand { get; set; }

    public string? DartCommand { get; set; }

    public string? WorkingDirectory { get; set; }
}
