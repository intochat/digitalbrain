using DigitalBrain.Kernel.Company;
using Ino = DigitalBrain.Ino;
using DigitalBrain.Kernel.Foundry;
using DigitalBrain.Kernel.Llm;
using DigitalBrain.Kernel.SelfEvolution;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Kernel;

public static class DigitalBrainKernelExtensions
{
    public static IHostApplicationBuilder UseDigitalBrainKernel(this IHostApplicationBuilder builder)
    {
        builder.UseOrleans(siloBuilder =>
        {
            siloBuilder.UseLocalhostClustering();
            siloBuilder.AddMemoryGrainStorageAsDefault();

            // Centralized prototype journals (single source in PrototypeJournals).
            siloBuilder.ConfigurePrototypeJournals();
            siloBuilder.AddFoundry();
            siloBuilder.ConfigureServices(services =>
            {
                services.AddSingleton<ISelfEvolutionApplyHandler, MarketplaceInstallApplyHandler>();
                services.AddSingleton<ISelfEvolutionApplyHandler, AutomationDefinitionApplyHandler>();
                services.AddSingleton<ISelfEvolutionApplyHandler, FoundryRunApplyHandler>();
                services.AddSingleton<ISelfEvolutionApplyHandler, FoundryDeployApplyHandler>();
                var inoRecallType = Type.GetType("DigitalBrain.Kernel.Ino.KernelInoCapabilityRecall");
if (inoRecallType != null)
    services.AddSingleton(typeof(Ino.IInoCapabilityRecall), inoRecallType);
            });

            // Built-in neurons discovered automatically.
        });

        builder.Services.AddDigitalBrainChat(builder.Configuration);
        builder.Services.AddSingleton<ProcessCrystallizer>(sp => new ProcessCrystallizer(sp.GetService<IChatClient>()));
        builder.Services.AddSingleton<SkillPackSynthesizer>();

        return builder;
    }

    public static IHostApplicationBuilder AddDigitalBrainClient(this IHostApplicationBuilder builder)
    {
        builder.UseOrleansClient();
        return builder;
    }
}




