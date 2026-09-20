using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.Coding;

public sealed class CodingModule : IModule
{
    public const string ConfigurationRoot = CodingModuleOptions.SectionName;
    public const string SolutionPathKey = CodingModuleOptions.SectionName + ":SolutionPath";
    public const string WorkspaceKeyKey = CodingModuleOptions.SectionName + ":WorkspaceKey";
    public const string TestProjectKey = CodingModuleOptions.SectionName + ":TestProject";

    public static ModuleDefinition Define(CodingModuleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new(typeof(CodingModule), new Dictionary<string, string?>
        {
            [SolutionPathKey] = options.SolutionPath,
            [WorkspaceKeyKey] = options.WorkspaceKey,
            [TestProjectKey] = options.TestProject,
        });
    }

    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<CodingModuleOptions>()
            .BindConfiguration(CodingModuleOptions.SectionName)
            .Validate(options => !string.IsNullOrWhiteSpace(options.WorkspaceKey), "Coding workspace key must not be empty.")
            .Validate(options => options.EditDeadline > TimeSpan.Zero, "Coding edit deadline must be positive.")
            .ValidateOnStart();
        services.TryAddSingleton<SolutionWorkspace>();
        services.TryAddSingleton<ISolutionLoader, MSBuildSolutionLoader>();
        services.TryAddSingleton<CodeFixCatalog>();
        services.TryAddSingleton<ChangeSetEditor>();
        services.TryAddSingleton<SolutionFileWatcher>();
        services.TryAddSingleton<IProcessRunner, ProcessRunner>();
        services.TryAddSingleton<DotnetRunner>();
        services.TryAddSingleton<GitRunner>();
        services.AddHostedService<WorkspaceWarmup>();
    }
}