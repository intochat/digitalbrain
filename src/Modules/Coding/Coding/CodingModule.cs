using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
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
        services.TryAddSingleton<IProcessRunner, ProcessRunner>();
        services.TryAddSingleton<GitRunner>();
        services.AddOptions<CodeExecutionOptions>().BindConfiguration(CodeExecutionOptions.SectionName);
        services.PostConfigure<CodeExecutionOptions>(o =>
        {
            if (o.ReferencePaths.Length == 0)
            {
                o.ReferencePaths = Directory.GetFiles(AppContext.BaseDirectory, "*.dll")
                    .Where(p => !Path.GetFileName(p).Contains(".Tests", StringComparison.Ordinal) && IsManagedAssembly(p)).ToArray();
                foreach (var (module, file) in new (string Module, string File)[]
                {
                    ("time", "DigitalBrain.Modules.Time.Contracts.dll"),
                    ("flutter", "DigitalBrain.Modules.Flutter.Contracts.dll"),
                    ("ai", "DigitalBrain.Modules.AI.Contracts.dll"),
                    ("google", "DigitalBrain.Modules.Google.Gmail.Contracts.dll"),
                })
                {
                    var path = Path.Combine(AppContext.BaseDirectory, file);
                    if (File.Exists(path)) { o.Modules.TryAdd(module, [path]); }
                }
            }
        });
        services.TryAddSingleton<CodeCheckCoordinator>();
        services.TryAddSingleton<ContractCatalog>();
        services.TryAddSingleton<ICodeArtifactStore>(sp => new CodeValidationService(sp.GetRequiredService<IOptions<CodeExecutionOptions>>().Value).Store());
        services.AddHostedService(sp => sp.GetRequiredService<CodeCheckCoordinator>());
    }

    private static bool IsManagedAssembly(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new System.Reflection.PortableExecutable.PEReader(stream);
        return reader.HasMetadata;
    }
}
