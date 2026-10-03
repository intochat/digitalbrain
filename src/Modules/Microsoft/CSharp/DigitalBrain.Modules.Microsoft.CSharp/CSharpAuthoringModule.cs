using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Enforcement;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.Microsoft.CSharp;

public sealed class CSharpAuthoringModule : IModule
{
    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddCSharpAuthoring();
        builder.Services.TryAddSingleton<CSharpSharing>();
        builder.Services.TryAddScoped(provider => provider.GetRequiredService<CSharpToolService>().ForScope(BrainScope.CurrentId()));
    }

    public void Configure(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        CSharpAuthoringEndpoints.Map(endpoints);
    }
}
