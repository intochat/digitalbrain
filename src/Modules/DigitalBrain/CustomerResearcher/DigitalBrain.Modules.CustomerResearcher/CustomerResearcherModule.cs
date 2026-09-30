using DigitalBrain.Core;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.CustomerResearcher;

[ModuleConfiguration(typeof(CustomerResearcherConfigurationContract))]
public sealed class CustomerResearcherModule : IModule
{
    public void Configure(ISiloBuilder silo)
    {
        ArgumentNullException.ThrowIfNull(silo);
        silo.Services.TryAddSingleton<ICompanyResearchAgent, CompanyResearchAgent>();
        silo.Services.TryAddSingleton<ICompanyResearchStore, CompanyResearchStore>();
    }

    public void Configure(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        CustomerResearcherEndpoints.Map(endpoints);
    }
}
