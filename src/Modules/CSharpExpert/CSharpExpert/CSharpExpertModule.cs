using DigitalBrain.Core;
using DigitalBrain.Microsoft.DotNet;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.CSharpExpert;

[ModuleConfiguration(typeof(CSharpExpertConfigurationContract))]
public sealed class CSharpExpertModule : IModule
{
    public const string WorkspaceRootKey = "DigitalBrain:CSharpExpert:WorkspaceRoot";

    public static ModuleDefinition Define(CSharpExpertModuleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new(typeof(CSharpExpertModule), new Dictionary<string, string?>
        {
            [WorkspaceRootKey] = options.WorkspaceRoot,
        });
    }

    public void Configure(ISiloBuilder silo)
    {
        silo.Services.TryAddSingleton<IProcessRunner, ProcessRunner>();
        silo.Services.TryAddSingleton<WorkspacePreparer>();
        silo.Services.AddOptions<CSharpExpertModuleOptions>().BindConfiguration("DigitalBrain:CSharpExpert");
        silo.Services.AddSingleton<SolutionPolicy>();
        silo.Services.AddSingleton<CodingRunHost>();
        silo.Services.TryAddSingleton<ICodingAgentBackend, AgentCodingBackend>();
    }

    public void Configure(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapCSharpExpert();
    }
}
