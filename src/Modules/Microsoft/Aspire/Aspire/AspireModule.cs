using DigitalBrain.Core;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.Microsoft.Aspire;

[ModuleConfiguration(typeof(AspireConfigurationContract))]
[ModuleHosting("DigitalBrain.Microsoft.Aspire.AspireModuleHosting, DigitalBrain.Modules.Microsoft.Aspire.Aspire.Hosting")]
public sealed class AspireModule : IModule
{
    public const string ConfigurationRoot = "DigitalBrain:Microsoft:Aspire";

    public static ModuleDefinition Define(AspireOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new(typeof(AspireModule), new Dictionary<string, string?>
        {
            [ConfigurationRoot + ":ApplicationName"] = options.ApplicationName,
        });
    }

    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddOptions<AspireOptions>().BindConfiguration(AspireOptions.SectionName);
        builder.Services.TryAddSingleton<AspireBridge>();
    }

    public void Configure(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapAspireBridge();
    }
}
