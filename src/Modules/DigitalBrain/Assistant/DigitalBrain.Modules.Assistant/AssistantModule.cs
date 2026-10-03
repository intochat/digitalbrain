using DigitalBrain.AI.Agents;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.AspNetCore;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.Assistant;

[ModuleId("assistant")]
public sealed class AssistantModule : IModule, IHttpModule
{
    public void Configure(ISiloBuilder silo)
    {
        ArgumentNullException.ThrowIfNull(silo);
        silo.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentToolFactory, WorkspaceFormTools>());
        silo.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentToolFactory, RegistryDiscoveryTools>());
        silo.Services.AddOptions<AssistantOptions>().BindConfiguration("Assistant");
    }

    public void Configure(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ComputeUsageEndpoints.Map(endpoints);
        AssistantEndpoints.Map(endpoints);
        AgentEndpoints.Map(endpoints);
    }
}
