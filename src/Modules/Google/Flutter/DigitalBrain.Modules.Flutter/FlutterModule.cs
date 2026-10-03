using DigitalBrain.Flutter.Workspace;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.AspNetCore;
using Microsoft.AspNetCore.Routing;
using Orleans.Hosting;

namespace DigitalBrain.Flutter;

[ModuleId("flutter")]
public sealed class FlutterModule : IModule<FlutterModuleOptions>, IHttpModule
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
