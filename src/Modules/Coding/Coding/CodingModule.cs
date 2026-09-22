using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.Coding;

[ModuleConfiguration(typeof(CodingConfigurationContract))]
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
            [ConfigurationRoot + ":EditDeadline"] = options.EditDeadline.ToString("c", System.Globalization.CultureInfo.InvariantCulture),
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
        services.AddOptions<CodeExecutionOptions>().BindConfiguration(CodeExecutionOptions.SectionName);
        services.PostConfigure<CodeExecutionOptions>(o =>
        {
            if (o.Root is not null && o.ReferencePaths.Length == 0)
            {
                o.ReferencePaths = Directory.GetFiles(AppContext.BaseDirectory, "*.dll")
                    .Where(p => !Path.GetFileName(p).Contains(".Tests", StringComparison.Ordinal) && IsManagedAssembly(p)).ToArray();
                foreach (var module in new[] { "Time", "Flutter", "AI", "Google" })
                {
                    var path = Path.Combine(AppContext.BaseDirectory, "DigitalBrain.Modules." + module + ".Contracts.dll");
                    if (File.Exists(path)) { o.Modules.TryAdd(module.ToLowerInvariant(), [path]); }
                }
            }
        });
        services.TryAddSingleton<CodeCheckCoordinator>();
        services.TryAddSingleton<ContractCatalog>();
        services.TryAddSingleton<ICodeArtifactStore>(sp => new CodeValidationService(sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CodeExecutionOptions>>().Value).Store());
        services.AddHostedService(sp => sp.GetRequiredService<CodeCheckCoordinator>());
        services.AddHostedService<WorkspaceWarmup>();
    }

    private static bool IsManagedAssembly(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new System.Reflection.PortableExecutable.PEReader(stream);
        return reader.HasMetadata;
    }
}
