using DigitalBrain.Core;
using DigitalBrain.Flutter.Inbox;
using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.Flutter;

[ModuleHosting("DigitalBrain.Flutter.Aspire.Hosting.FlutterModuleHosting, DigitalBrain.Modules.Flutter.Aspire.Hosting")]
[ModuleConfiguration(typeof(FlutterConfigurationContract))]
public sealed class FlutterModule : IModule
{
    public static ModuleDefinition Define(FlutterModuleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!Enum.IsDefined(options.Hosting.Kind)) { throw new ArgumentOutOfRangeException(nameof(options)); }
        if (string.IsNullOrWhiteSpace(options.Hosting.ResourceName) || string.IsNullOrWhiteSpace(options.Hosting.ShellName)
            || string.IsNullOrWhiteSpace(options.Hosting.ChatName))
        { throw new ArgumentException("Flutter resource, shell and chat names must be specified.", nameof(options)); }
        return new(typeof(FlutterModule), new Dictionary<string, string?>
        {
            ["DigitalBrain:Flutter:Hosting:Kind"] = options.Hosting.Kind.ToString(),
            ["DigitalBrain:Flutter:Hosting:ResourceName"] = options.Hosting.ResourceName,
            ["DigitalBrain:Flutter:Hosting:DeviceTarget"] = options.Hosting.DeviceTarget,
            ["DigitalBrain:Flutter:Hosting:ShellName"] = options.Hosting.ShellName,
            ["DigitalBrain:Flutter:Hosting:ChatName"] = options.Hosting.ChatName,
            ["DigitalBrain:Flutter:Hosting:FlutterCommand"] = options.Hosting.FlutterCommand ?? "",
            ["DigitalBrain:Flutter:Hosting:WorkingDirectory"] = options.Hosting.WorkingDirectory ?? "",
            ["DigitalBrain:Flutter:Hosting:ReleaseBuild"] = options.Hosting.ReleaseBuild.ToString(),
        });
    }
    public const string InboxPath = "/ui/inbox";
    public const string InboxGrain = "ui";

    public void Configure(ISiloBuilder silo)
    {
        ArgumentNullException.ThrowIfNull(silo);
    }

    public void Configure(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapInbox();
        endpoints.MapUiKit();
    }
}
