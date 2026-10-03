using DigitalBrain.Kernel.AspNetCore;
using DigitalBrain.Kernel;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.Microsoft.Aspire;

[ModuleId("aspire")]
public sealed class AspireModule : IModule<AspireOptions>, IHttpModule
{
    public const string ConfigurationRoot = "DigitalBrain:Microsoft:Aspire";

    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddOptions<AspireOptions>()
            .Configure<IConfiguration>((options, configuration) => configuration.PopulateModuleOptions("aspire", options));
        builder.Services.TryAddSingleton<AspireBridge>();
    }

    public void Configure(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapAspireBridge();
    }
}
