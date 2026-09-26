using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace IntoChat.Apps;

[ModuleConfiguration(typeof(LeadGeneratorConfigurationContract))]
public sealed class LeadGeneratorModule : IModule
{
    public static ModuleDefinition Define() => new(typeof(LeadGeneratorModule));

    public void Configure(ISiloBuilder silo)
    {
        silo.Services.TryAddSingleton(TimeProvider.System);
        silo.Services.TryAddSingleton<IWebResearchProvider, DeterministicWebResearchProvider>();
        silo.Services.TryAddSingleton<IWebResearch, WebResearchService>();
    }
}

public sealed class LeadGeneratorModuleOptions;

public sealed class LeadGeneratorConfigurationContract() : ModuleConfigurationContract<LeadGeneratorModule, LeadGeneratorModuleOptions>
{
    protected override ModuleDefinition Compile(LeadGeneratorModuleOptions options) => LeadGeneratorModule.Define();
}
