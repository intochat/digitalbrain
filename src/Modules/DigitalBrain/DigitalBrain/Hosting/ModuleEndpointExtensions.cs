using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Core;

public static class ModuleEndpointExtensions
{
    public static IEndpointRouteBuilder MapModuleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        foreach (var module in endpoints.ServiceProvider.GetServices<IModule>())
        {
            module.Configure(endpoints);
        }

        return endpoints;
    }
}
