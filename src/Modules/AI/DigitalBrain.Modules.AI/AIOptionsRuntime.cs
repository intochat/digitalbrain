using DigitalBrain.Kernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
namespace DigitalBrain.AI;

internal static class AIOptionsRuntime
{
    internal static AIOptions Read(IConfiguration configuration)
    {
        var options = configuration.GetModuleOptions<AIOptions>("ai");
        configuration.GetSection(AIOptions.SectionName).Bind(options);
        ProjectOllamaModelMarkers(options, configuration);
        return options;
    }
    internal static void Register(IServiceCollection services)
    {
        services.AddOptions<AIOptions>().Configure<IConfiguration>((options, configuration) => options.CopyFrom(Read(configuration)));
        services.AddOptions<AIWorkspaceOptions>().BindConfiguration(AIWorkspaceOptions.SectionName);
    }
    private static void ProjectOllamaModelMarkers(AIOptions options, IConfiguration configuration)
    {
        foreach (var section in configuration.GetSection($"{AIOptions.SectionName}:Ollama").GetChildren())
        {
            if (section["Model"] is { } model)
            {
                options.Ollama.Models[section.Key] = new AIModelOptions { Model = model };
            }
        }
    }
}
