using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.Microsoft.CSharp;

[ModuleConfiguration(typeof(CSharpConfigurationContract))]
[ModuleHosting("DigitalBrain.Microsoft.CSharp.CSharpModuleHosting, DigitalBrain.Modules.Microsoft.CSharp.Aspire.Hosting")]
public sealed class CSharpModule : IModule
{
    public static ModuleDefinition Define(CSharpOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new(typeof(CSharpModule), new Dictionary<string, string?>
        {
            [CSharpOptions.SectionName + ":SourceRoot"] = options.SourceRoot,
        });
    }

    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddOptions<CSharpOptions>().BindConfiguration(CSharpOptions.SectionName).PostConfigure(options =>
        {
            // Unset composition values arrive as empty strings, not nulls.
            if (string.IsNullOrWhiteSpace(options.SourceRoot)) { options.SourceRoot = FindRepositoryRoot(); }
        });
        builder.Services.AddHttpClient<SandboxCSharpRunner>();
        builder.Services.TryAddSingleton<CSharpContractCatalog>();
    }

    internal static string? FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx"))) { return directory.FullName; }
        }
        return null;
    }
}
