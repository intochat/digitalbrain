using DigitalBrain.Kernel.Foundry;
using DigitalBrain.Kernel.Llm;
using DigitalBrain.Kernel.SelfEvolution;
using Ino = DigitalBrain.Ino;

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
            siloBuilder.ConfigureServices(services =>
            {
                services.AddSingleton<ISelfEvolutionApplyHandler, AutomationDefinitionApplyHandler>();
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




