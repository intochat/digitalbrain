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
    public const string DefaultOwner = "dev";
    public const string DefaultDeviceTarget = "windows";
    // web-server is Flutter's headless web device: it serves the app over HTTP without driving
    // a browser of its own, so the fixed FlutterWebPort below is a real, addressable endpoint.
    // The "chrome" device never prints or exposes a served URL (it drives its own tool-launched
    // Chrome instance), which made WithWebHost unreachable for automation; see task-4-report.md.
    public const string DefaultWebDeviceTarget = "web-server";
    public const string WebPlatformDirectoryName = "web";
    public const string HttpEndpointName = "http";

    // Local-only DDS so AppHost can hot-reload without scraping a new URI each run.
    public const int FlutterDdsPort = 54721;
    public const int FlutterVmServicePort = 54722;
    // Fixed, unproxied serving port for the web host (--web-port), following the two ports
    // above: local-only, stable across runs, and registered as the flutter resource's real
    // Aspire HTTP endpoint so health checks and endpoint lookups work without log scraping.
    public const int FlutterWebPort = 54723;
    public const string FlutterWebHostname = "127.0.0.1";
}
