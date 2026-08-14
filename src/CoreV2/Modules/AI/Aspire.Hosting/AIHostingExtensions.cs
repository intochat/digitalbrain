using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Brain.Modules.AI.Contracts;
using DigitalBrain.Aspire.Hosting;

namespace Brain.Modules.AI.Aspire.Hosting;

public static class AIHostingExtensions
{
    public const string EndpointEnvironmentKey = "DigitalBrain__AI__Ollama__Endpoint";
    public const string ModelEnvironmentKey = "DigitalBrain__AI__Ollama__Model";

    public static DigitalBrainModuleBuilder<AiModule> WithGemma4(
        this DigitalBrainModuleBuilder<AiModule> module)
    {
        ArgumentNullException.ThrowIfNull(module);
        var state = module.Brain.GetOrAddState(
            static brain => new AIHostingState(brain),
            out var added);
        if (!added)
        {
            throw new InvalidOperationException(
                $"AI is already configured on brain '{module.Brain.Name}'. Configure its model exactly once.");
        }

        module.AddProjection(state);
        return module;
    }

    private sealed class AIHostingState : DigitalBrainModuleProjection
    {
        private readonly IResourceBuilder<OllamaModelResource> _model;

        public AIHostingState(DigitalBrainBuilder brain)
        {
            var ollama = brain.ApplicationBuilder
                .AddOllama("ollama")
                .WithGPUSupport()
                .WithDataVolume()
                .WithLifetime(ContainerLifetime.Persistent)
                .WithEnvironment("OLLAMA_KEEP_ALIVE", "-1");
            _model = ollama.AddModel("gemma4-12b", "gemma4:12b");
        }

        public override void ApplyToRuntime<TResource>(IResourceBuilder<TResource> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder
                .WithAnnotation(new WaitAnnotation(
                    _model.Resource,
                    WaitType.WaitUntilHealthy,
                    exitCode: 0))
                .WithEnvironment(EndpointEnvironmentKey, _model.Resource.Parent.UriExpression)
                .WithEnvironment(ModelEnvironmentKey, _model.Resource.ModelName);
        }
    }
}
