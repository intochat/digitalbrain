using Ino = DigitalBrain.Ino;
using DigitalBrain.Kernel.Foundry;
using DigitalBrain.Kernel.Llm;
using DigitalBrain.Kernel.SelfEvolution;

namespace DigitalBrain.Kernel;

public static class DigitalBrainKernelExtensions
{
    public static IHostApplicationBuilder UseDigitalBrainKernel(this IHostApplicationBuilder builder)
    {
        builder.UseOrleans(siloBuilder =>
        {
            siloBuilder.UseLocalhostClustering();
            siloBuilder.AddMemoryGrainStorageAsDefault();

            // Prototype journals retained only for legacy !isAspireHosted fast-paths (Program.cs non-aspire).
            // Aspire paths use real journal blobs exclusively.
            siloBuilder.AddFoundry();
            siloBuilder.ConfigureServices(services =>
            {
                services.AddSingleton<ISelfEvolutionApplyHandler, MarketplaceInstallApplyHandler>();
                services.AddSingleton<ISelfEvolutionApplyHandler, AutomationDefinitionApplyHandler>();
                services.AddSingleton<ISelfEvolutionApplyHandler, FoundryRunApplyHandler>();
                services.AddSingleton<ISelfEvolutionApplyHandler, FoundryDeployApplyHandler>();
                services.AddSingleton<DigitalBrain.Ino.IInoCapabilityRecall, DigitalBrain.Ino.InoCapabilityRecall>();
                services.AddSingleton<ICapabilityBroker, CapabilityBroker>();
            });

            // Built-in neurons discovered automatically.
        });

        builder.Services.AddDigitalBrainChat(builder.Configuration);

        return builder;
    }

    public static IHostApplicationBuilder AddDigitalBrainClient(this IHostApplicationBuilder builder)
    {
        builder.UseOrleansClient();
        return builder;
    }
}




