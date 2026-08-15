namespace Brain.Modules.UI.Aspire.Hosting;

public sealed class FlutterHostOptions
{
    public string ResourceName { get; set; } = ShellNames.DefaultFlutterResourceName;

    public string DeviceTarget { get; set; } = ShellNames.DefaultDeviceTarget;

    public string ShellName { get; set; } = ShellNames.DefaultShellName;

    public string? FlutterCommand { get; set; }

    public string? DartCommand { get; set; }

    public string? WorkingDirectory { get; set; }
}
