using DigitalBrain.AI.Agents;
using DigitalBrain.Core;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.Assistant;

public sealed class AssistantModule : IModule
{
    public void Configure(ISiloBuilder silo)
    {
        ArgumentNullException.ThrowIfNull(silo);
        silo.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentToolFactory, WorkspaceFormTools>());
        silo.Services.TryAddSingleton<PackageService>();
    }

    public void Configure(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ComputeUsageEndpoints.Map(endpoints);
        AssistantEndpoints.Map(endpoints);
        AgentEndpoints.Map(endpoints);
        PackageEndpoints.Map(endpoints);
        if (endpoints.ServiceProvider.GetServices<IAgentToolFactory>().Any(factory => factory is DigitalBrain.Microsoft.CSharp.CSharpAgentTools))
        { PackageEndpoints.MapAuthoring(endpoints); }
    }
}
