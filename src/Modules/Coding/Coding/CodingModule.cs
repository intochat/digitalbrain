using DigitalBrain.AI;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Coding;

public sealed class CodingModule : IModule
{
    public const string ConfigurationRoot = "DigitalBrain:Coding";
    public const string SolutionPathKey = "DigitalBrain:Coding:SolutionPath";
    public const string WorkspaceKeyKey = "DigitalBrain:Coding:WorkspaceKey";
    public const string TestProjectKey = "DigitalBrain:Coding:TestProject";

    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton<CodingModule>();
        builder.Services.TryAddSingleton<SolutionWorkspace>();
        builder.Services.TryAddSingleton<ISolutionLoader, MSBuildSolutionLoader>();
        builder.Services.TryAddSingleton<CodeFixCatalog>();
        builder.Services.TryAddSingleton<ChangeSetEditor>();
        builder.Services.TryAddSingleton<SolutionFileWatcher>();
        builder.Services.TryAddSingleton<IProcessRunner, ProcessRunner>();
        builder.Services.TryAddSingleton<DotnetRunner>();
        builder.Services.TryAddSingleton<GitRunner>();
        builder.Services.TryAddSingleton(CodingToolOptions.Default);
        builder.Services.AddHostedService<WorkspaceWarmup>();

        builder.Services.AddSingleton<CodingNativeTools>();
        foreach (var tool in new[]
        {
            "code_find_symbols", "code_references", "code_diagnostics", "code_map", "code_skeleton", "code_member", "code_callers",
            "code_implementations", "code_propose_edit", "code_check", "code_commit", "code_build", "code_test",
        })
        {
            builder.Services.AddNativeTool(tool, services => services.GetRequiredService<CodingNativeTools>().Named(tool));
        }
    }
}
