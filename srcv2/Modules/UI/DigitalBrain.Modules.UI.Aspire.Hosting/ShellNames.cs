namespace DigitalBrain.UI.Aspire.Hosting;

public static class ShellNames
{
    public const string DefaultFlutterResourceName = "flutter";
    public const string UIBaseEnvironmentVariable = "DIGITALBRAIN_UI_BASE";
    public const string ShellEnvironmentVariable = "DIGITALBRAIN_SHELL";
    public const string ChatEnvironmentVariable = "DIGITALBRAIN_CHAT";
    public const string OwnerEnvironmentVariable = "DigitalBrain__Owner";
    public const string FlutterCommandEnvironmentVariable = "FLUTTER_COMMAND";
    public const string DartCommandEnvironmentVariable = "DART_COMMAND";
    public const string HeadlessHostEntry = "bin/digitalbrain_host.dart";
    public const string DefaultShellName = "desk";
    public const string DefaultChatName = "main";
    public const string DefaultOwner = DigitalBrain.Aspire.Hosting.DigitalBrainNames.DefaultOwner;
    public const string DefaultDeviceTarget = "windows";
    public const string DefaultWebDeviceTarget = "chrome";
    public const string WebPlatformDirectoryName = "web";
    public const string HttpEndpointName = "http";

    // Local-only DDS so AppHost can hot-reload without scraping a new URI each run.
    public const int FlutterDdsPort = 54721;
    public const int FlutterVmServicePort = 54722;
}