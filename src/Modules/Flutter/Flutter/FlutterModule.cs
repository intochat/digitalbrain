using DigitalBrain.Core;
using DigitalBrain.Flutter.Inbox;
using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.Flutter;

public sealed class FlutterModule : IModule
{
    public static ModuleDefinition Define(FlutterModuleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!Enum.IsDefined(options.Hosting.Kind)) { throw new ArgumentOutOfRangeException(nameof(options)); }
        return new(typeof(FlutterModule), new Dictionary<string, string?>
        { ["DigitalBrain:Flutter:Hosting:Kind"] = options.Hosting.Kind.ToString() });
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
