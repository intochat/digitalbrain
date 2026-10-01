using DigitalBrain.AI.Agents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Microsoft.CSharp;

public static class CSharpAuthoringHosting
{
    public static IServiceCollection AddCSharpAuthoring(this IServiceCollection services)
    {
        services.TryAddSingleton<CSharpCatalogStore>();
        services.TryAddSingleton<CSharpToolService>();
        services.TryAddSingleton<DigitalBrain.Apps.IScriptSandbox, CSharpScriptSandbox>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentToolFactory, CSharpAgentTools>());
        return services;
    }
}
