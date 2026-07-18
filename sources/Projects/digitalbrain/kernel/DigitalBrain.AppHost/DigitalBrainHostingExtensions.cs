using DigitalBrain.Hosting.DigitalBrain;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Hosting;

public static class DigitalBrainHostingExtensions
{
    public static IDigitalBrainBuilder AddDigitalBrain(
        this IDistributedApplicationBuilder builder,
        string name = "digitalbrain")
    {
        // 1. Parse Profile Configuration
        var args = Environment.GetCommandLineArgs();
        var config = ProfileConfiguration.Parse(builder.Configuration, args);

        // 2. Underlying DigitalBrain substrate
        var digitalbrain = AddDigitalBrainExtensions.AddDigitalBrain(builder, name);

        var ollama = builder.AddOllama("ino-llm")
            .WithContainerRuntimeArgs("--gpus", "all")
            .WithDataVolume();
        var localModel = ollama.AddModel("ino-local", "nemotron-mini");
        var synthModel = ollama.AddModel("ino-synth", "phi4");

        if (digitalbrain.Kernel is not null)
        {
            digitalbrain.Kernel
                .WithReference(localModel).WaitFor(localModel)
                .WithReference(synthModel).WaitFor(synthModel);
        }

        InoTopologyParser.LoadDynamicTopology(builder, digitalbrain, "digitalbrain.ino");



        var digitalBrainBuilder = new DigitalBrainBuilder(digitalbrain, config);
        
        // 4. Wire standard configurations
        digitalBrainBuilder.ApplyConfigurations();

        builder.Services.AddHostedService<AspireResourceStateMonitor>();

        return digitalBrainBuilder;
    }
}
