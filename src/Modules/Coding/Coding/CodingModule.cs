using DigitalBrain.AI;
using DigitalBrain.Core;
using DigitalBrain.Microsoft;
using Microsoft.Extensions.Configuration;
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
        builder.Services.TryAddSingleton(SlotOptions.From(builder.Configuration));
        builder.Services.TryAddSingleton<ISlotBuilder, SlotBuilder>();
        // 60s: a debounced gateway switch can wait out the gateway's minimum interval before answering.
        // A typed client, so every probe shares one handler and the credential is applied in one place.
        builder.Services.AddHttpClient<ISlotEndpoints, HttpSlotEndpoints>(HttpSlotEndpoints.HttpClientName,
            static (services, client) =>
            {
                client.Timeout = TimeSpan.FromSeconds(60);
                client.DefaultRequestHeaders.Authorization = SlotProbeCredential.Of(services.GetRequiredService<IConfiguration>());
            });
        // The Microsoft module registers the real one with AddSingleton, which wins in either composition
        // order: it is skipped by this TryAdd when it ran first, and resolved last when it runs after.
        builder.Services.TryAddSingleton<IAspireResourceCommands, UnconfiguredAspireResourceCommands>();
        builder.Services.AddHostedService<WorkspaceWarmup>();

        builder.Services.AddSingleton<CodingNativeTools>();
        foreach (var tool in new[]
        {
            "code_find_symbols", "code_references", "code_diagnostics", "code_map", "code_skeleton", "code_member", "code_callers",
            "code_implementations", "code_derived", "code_propose_edit", "code_check", "code_commit", "code_build", "code_test",
            "code_promote",
        })
        {
            builder.Services.AddNativeTool(tool, services => services.GetRequiredService<CodingNativeTools>().Named(tool));
        }
    }
}
