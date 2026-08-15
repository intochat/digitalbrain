namespace DigitalBrain.UI.Aspire.Hosting;

public sealed class FlutterHostOptions
{
    public string ResourceName { get; set; } = ShellHostingExtensions.DefaultFlutterResourceName;

    public string DeviceTarget { get; set; } = ShellHostingExtensions.DefaultDeviceTarget;

    public string ShellName { get; set; } = ShellHostingExtensions.DefaultShellName;

    public string ChatName { get; set; } = ShellHostingExtensions.DefaultChatName;

    public string? FlutterCommand { get; set; }

    public string? DartCommand { get; set; }

    public string? WorkingDirectory { get; set; }
}
