using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Orleans.Dashboard;

namespace DigitalBrain.DevTools;

public static class DevToolsHosting
{
    public const string DashboardPath = "/dashboard";

    public static ISiloBuilder AddDigitalBrainDevTools(this ISiloBuilder builder, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(environment);

        if (environment.IsDevelopment())
        {
            builder.AddDashboard();
        }

        return builder;
    }

    public static IEndpointRouteBuilder MapDigitalBrainDevTools(this IEndpointRouteBuilder endpoints, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(environment);

        if (environment.IsDevelopment())
        {
            endpoints.MapOrleansDashboard(DashboardPath);
        }

        return endpoints;
    }
}
