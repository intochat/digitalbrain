using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.Microsoft.Roslyn;

public sealed class RoslynModule : IModule
{
    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<RoslynModuleOptions>()
            .BindConfiguration(RoslynModuleOptions.SectionName)
            .Validate(options => !string.IsNullOrWhiteSpace(options.WorkspaceKey), "Roslyn workspace key must not be empty.")
            .ValidateOnStart();
        services.TryAddSingleton<SolutionWorkspace>();
        services.TryAddSingleton<ISolutionLoader, MSBuildSolutionLoader>();
        services.TryAddSingleton<CodeFixCatalog>();
        services.TryAddSingleton<ChangeSetEditor>();
        services.TryAddSingleton<SolutionFileWatcher>();
        services.AddHostedService<WorkspaceWarmup>();
    }
}
