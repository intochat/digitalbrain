using DigitalBrain.Core;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;

namespace DigitalBrain.CSharpExpert;

[ModuleConfiguration(typeof(CSharpExpertConfigurationContract))]
public sealed class CSharpExpertModule : IModule
{
    public static ModuleDefinition Define() => new(typeof(CSharpExpertModule));

    public void Configure(ISiloBuilder silo)
    {
        silo.Services.AddSingleton<CodingRunHost>();
    }

    public void Configure(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapCSharpExpert();
    }
}
