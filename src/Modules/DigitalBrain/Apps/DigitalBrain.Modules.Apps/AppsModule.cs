using DigitalBrain.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.Apps;

public sealed class AppsModule : IModule
{
    public void Configure(ISiloBuilder silo)
    {
        ArgumentNullException.ThrowIfNull(silo);
        silo.Services.TryAddSingleton(TimeProvider.System);
        silo.Services.TryAddSingleton<AppRequirements>();
        silo.Services.TryAddSingleton<ITestScriptRunner, CSharpFileTestRunner>();
        silo.Services.TryAddSingleton<MarketplaceService>();
        silo.Services.TryAddSingleton<AppService>();
        silo.Services.TryAddSingleton<AppPublishing>();
        silo.Services.TryAddSingleton<AppAuthoringPolicy>();
        silo.Services.AddAppRuntime<GroupChatRuntime>();
        silo.Services.AddAppRuntime<PromptRuntime>();
    }

    public void Configure(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapGet("/session/capabilities", static (IServiceProvider services) =>
            Results.Ok(new { developerMode = services.GetService<IScriptSandbox>()?.CanRun == true }));
        AppsEndpoints.Map(endpoints);
        PackageEndpoints.Map(endpoints);
        MarketplaceEndpoints.MapMarketplace(endpoints);
    }
}
