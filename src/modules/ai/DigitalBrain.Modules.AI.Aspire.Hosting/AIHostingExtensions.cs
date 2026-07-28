using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.OpenAI;
using DigitalBrain.AI.Ollama;
using DigitalBrain.AI.OpenAI;
using DigitalBrain.Aspire.Hosting;

namespace DigitalBrain.AI.Aspire.Hosting;

public static class AIHostingExtensions
{
    public static DigitalBrainModuleBuilder<AIModule> WithLlm<TModel>(
        this DigitalBrainModuleBuilder<AIModule> module)
        where TModel : class, ILLM
    {
        ArgumentNullException.ThrowIfNull(module);

        var state = module.Brain.GetOrAddState(
            static brain => new AIHostingState(brain),
            out var added);
        if (added)
        {
            module.RequireStateProtection();
            module.AddProjection(state);
        }

        state.Add<TModel>();
        return module;
    }

    private sealed class AIHostingState(DigitalBrainBuilder brain) : DigitalBrainModuleProjection
    {
        private const string OllamaImageTag = "latest";

        private static readonly Dictionary<Type, (string ResourceSuffix, string Tag)> OllamaModelCatalog = new()
        {
            [typeof(Llama32)] = ("llama32", "llama3.2"),
            [typeof(Gemma4)] = ("gemma4", "gemma4:12b"),
            [typeof(Qwen35)] = ("qwen35", "qwen3.5:9b"),
            [typeof(Granite41)] = ("granite41", "granite4.1:8b"),
        };

        private readonly HashSet<Type> _models = [];
        private readonly Dictionary<Type, IResourceBuilder<OllamaModelResource>> _ollamaModels = [];
        private IResourceBuilder<OllamaResource>? _ollama;
        private IResourceBuilder<OpenAIResource>? _openAI;
        private IResourceBuilder<OpenAIModelResource>? _gpt56;
        private IResourceBuilder<ParameterResource>? _openAIKey;

        internal void Add<TModel>()
            where TModel : class, ILLM
        {
            var model = typeof(TModel);

            if (!_models.Add(model))
            {
                throw new InvalidOperationException(
                    $"{model.FullName} is already configured on brain '{brain.Name}'. Add each model exactly once.");
            }

            if (OllamaModelCatalog.TryGetValue(model, out var ollama))
            {
                AddOllamaModel(model, ollama.ResourceSuffix, ollama.Tag);
                return;
            }

            if (model == typeof(Gpt56))
            {
                AddGpt56();
                return;
            }

            throw new NotSupportedException(
                $"{model.FullName} has no Aspire integration. The AI module must own the provider resource for every concrete LLM.");
        }

        public override void Apply<TResource>(IResourceBuilder<TResource> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            foreach (var (model, resource) in _ollamaModels)
            {
                builder
                    .WithAnnotation(new WaitAnnotation(
                        resource.Resource,
                        WaitType.WaitUntilHealthy,
                        exitCode: 0))
                    .WithEnvironment(
                        "DigitalBrain__AI__Ollama__Endpoint",
                        resource.Resource.Parent.UriExpression)
                    .WithEnvironment(
                        $"DigitalBrain__AI__Ollama__{model.Name}__Model",
                        resource.Resource.ModelName);
            }

            if (_gpt56 is not null)
            {
                builder
                    .WithEnvironment(
                        "DigitalBrain__AI__OpenAI__ApiKey",
                        _openAIKey!)
                    .WithEnvironment(
                        "DigitalBrain__AI__OpenAI__Endpoint",
                        _gpt56.Resource.Parent.UriExpression)
                    .WithEnvironment(
                        "DigitalBrain__AI__OpenAI__Gpt56__Model",
                        _gpt56.Resource.Model);
            }
        }

        private void AddOllamaModel(Type model, string resourceSuffix, string tag)
        {
            var builder = brain.GetApplicationBuilder();
            _ollama ??= builder
                .AddOllama($"{brain.Name}-ai-ollama")
                .WithImageTag(OllamaImageTag)
                .WithGPUSupport()
                .WithDataVolume()
                .WithLifetime(ContainerLifetime.Persistent)
                .WithOpenWebUI(uiContainer => uiContainer.WithLifetime(ContainerLifetime.Persistent));

            _ollamaModels[model] = _ollama.AddModel(
                $"{brain.Name}-ai-{resourceSuffix}",
                tag);
        }

        private void AddGpt56()
        {
            var builder = brain.GetApplicationBuilder();
            _openAIKey ??= builder
                .AddParameter($"{brain.Name}-ai-openai-api-key", secret: true)
                .WithDescription(
                    "Create or manage an API key at [OpenAI Platform](https://platform.openai.com/api-keys).",
                    enableMarkdown: true);
            _openAI ??= builder
                .AddOpenAI($"{brain.Name}-ai-openai")
                .WithApiKey(_openAIKey);
            _gpt56 = _openAI.AddModel($"{brain.Name}-ai-gpt56", "gpt-5.6");
        }
    }
}
