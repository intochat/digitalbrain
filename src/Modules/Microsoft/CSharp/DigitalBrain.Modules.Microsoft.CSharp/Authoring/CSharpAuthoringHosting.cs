using DigitalBrain.AI.Agents;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Microsoft.CSharp;

// Workspace C# files are listed and described even where CSharpModule is not composed, so the host
// registers authoring itself; running a file still needs the module's contract catalog.
public static class CSharpAuthoringHosting
{
    public static IServiceCollection AddCSharpAuthoring(this IServiceCollection services)
    {
        services.AddSingleton<CSharpCatalogStore>();
        services.AddSingleton<CSharpToolService>();
        services.AddSingleton<IAgentToolFactory, CSharpAgentTools>();
        return services;
    }
}
