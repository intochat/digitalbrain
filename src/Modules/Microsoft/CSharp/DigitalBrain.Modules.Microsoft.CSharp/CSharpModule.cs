using Azure.Core;
using Azure.Identity;
using DigitalBrain.Core;
using DigitalBrain.Core.Enforcement;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.Microsoft.CSharp;

[ModuleDeployment("DigitalBrain.Microsoft.CSharp.CSharpDeployment, DigitalBrain.Modules.Microsoft.CSharp.Deployment")]
public sealed class CSharpModule : IModule<CSharpOptions>
{
    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddOptions<CSharpOptions>()
            .Configure<IConfiguration>((options, configuration) => configuration.PopulateModuleOptions(nameof(CSharpModule), options))
            .PostConfigure(options =>
        {
            if (string.IsNullOrWhiteSpace(options.SourceRoot)) { options.SourceRoot = FindRepositoryRoot(); }
        });
        builder.Services.AddOptions<CSharpDeploymentSettings>().BindConfiguration(CSharpDeploymentSettings.SectionName);
        builder.Services.AddCSharpAuthoring();
        builder.Services.TryAddSingleton<CSharpSharing>();
        builder.Services.TryAddScoped(provider => provider.GetRequiredService<CSharpToolService>().ForScope(BrainScope.CurrentId()));
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<RunTokens>();
        builder.Services.TryAddSingleton<ScriptRunEnvironment>();
        // Explicit: DI would resolve the assemblies parameter as an empty IEnumerable<Assembly>.
        builder.Services.TryAddSingleton(provider => new ScriptContracts(provider.GetRequiredService<ModuleInventory>().ContractAssemblies()));
        builder.Services.TryAddTransient<ScriptEdge>();
        builder.Services.TryAddSingleton<CSharpDirectives>();
        builder.Services.TryAddSingleton<CSharpContractDiscovery>();
        builder.Services.TryAddSingleton<CSharpScriptCheck>();
        if (!string.IsNullOrWhiteSpace(builder.Configuration[CSharpDeploymentSettings.SectionName + ":SessionPoolEndpoint"]))
        {
            builder.Services.TryAddSingleton<TokenCredential>(_ => new DefaultAzureCredential());
            builder.Services.AddHttpClient<ICSharpRunner, SessionPoolRunner>();
        }
        else { builder.Services.AddHttpClient<ICSharpRunner, AspireSandboxRunner>(); }
    }

    public void Configure(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapScriptEdge();
        CSharpAuthoringEndpoints.Map(endpoints);
    }

    public static string? FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx"))) { return directory.FullName; }
        }
        return null;
    }
}
