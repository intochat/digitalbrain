using DigitalBrain.Flutter.Workspace;
using DigitalBrain.Core;
using Microsoft.AspNetCore.Routing;
using Orleans.Hosting;

namespace DigitalBrain.Flutter;

public sealed class FlutterModule : IModule<FlutterModuleOptions>
{
    public void Configure(ISiloBuilder silo)
    {
        ArgumentNullException.ThrowIfNull(silo);
    }

    public void Configure(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapUiKit();
        endpoints.MapShellPersistence();
        WorkspaceEndpoints.Map(endpoints);
        AppUiEndpoints.Map(endpoints);
    }
}