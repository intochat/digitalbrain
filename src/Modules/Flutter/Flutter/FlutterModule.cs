using DigitalBrain.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
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
        endpoints.MapGet(InboxPath, static async Task<IResult> (IGrainFactory grains, CancellationToken cancellationToken) =>
        {
            var lines = await grains.GetGrain<IInbox>(InboxGrain).Read().WaitAsync(cancellationToken);
            return Results.Ok(lines);
        });
    }
}
