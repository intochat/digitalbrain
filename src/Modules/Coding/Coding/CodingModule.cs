using DigitalBrain.AI;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Coding;

public sealed class CodingModule : IModule
{
    public const string ConfigurationRoot = CodingOptions.SectionName;
    public const string SolutionPathKey = "DigitalBrain:Coding:SolutionPath";
    public const string WorkspaceKeyKey = "DigitalBrain:Coding:WorkspaceKey";
    public const string TestProjectKey = "DigitalBrain:Coding:TestProject";

    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddOptions<CodingOptions>()
            .BindConfiguration(CodingOptions.SectionName)
            .Validate(options => !string.IsNullOrWhiteSpace(options.WorkspaceKey), "Coding workspace key must not be empty.")
            .ValidateOnStart();
        builder.Services.AddOptions<CodingToolOptions>()
            .BindConfiguration(CodingToolOptions.SectionName)
            .Validate(options => options.ReactionWait > TimeSpan.Zero && options.EditDeadline > TimeSpan.Zero,
                "Coding tool deadlines must be positive.")
            .ValidateOnStart();
        builder.Services.TryAddSingleton<CodingModule>();
        builder.Services.TryAddSingleton<SolutionWorkspace>();
        builder.Services.TryAddSingleton<ISolutionLoader, MSBuildSolutionLoader>();
        builder.Services.TryAddSingleton<CodeFixCatalog>();
        builder.Services.TryAddSingleton<ChangeSetEditor>();
        builder.Services.TryAddSingleton<SolutionFileWatcher>();
        builder.Services.TryAddSingleton<IProcessRunner, ProcessRunner>();
        builder.Services.TryAddSingleton<DotnetRunner>();
        builder.Services.TryAddSingleton<GitRunner>();
        builder.Services.TryAddSingleton(services => services.GetRequiredService<IOptions<CodingToolOptions>>().Value);
        builder.Services.AddHostedService<WorkspaceWarmup>();

        builder.Services.AddSingleton(services => new CodingNativeTools(
            services.GetRequiredService<SolutionWorkspace>(), services.GetRequiredService<IGrainFactory>(),
            services.GetRequiredService<DotnetRunner>(), services.GetRequiredService<GitRunner>(),
            services.GetRequiredService<IOptions<CodingOptions>>(), services.GetRequiredService<CodingToolOptions>()));
        foreach (var tool in new[]
        {
            "code_find_symbols", "code_references", "code_diagnostics", "code_map", "code_skeleton", "code_member", "code_callers",
            "code_implementations", "code_derived", "code_propose_edit", "code_check", "code_commit", "code_build", "code_test",
        })
        {
            builder.Services.AddNativeTool(tool, services => services.GetRequiredService<CodingNativeTools>().Named(tool));
        }
    }
}
