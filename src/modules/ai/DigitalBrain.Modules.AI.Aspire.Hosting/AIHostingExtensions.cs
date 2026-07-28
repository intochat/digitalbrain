using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.OpenAI;
using DigitalBrain.AI.Ollama;
using DigitalBrain.AI.OpenAI;
using DigitalBrain.Aspire.Hosting;

namespace DigitalBrain.AI.Aspire.Hosting;

public static class AIHostingExtensions
{
    public const string Llama32Feature = "ai.llm.llama32";
    public const string Gemma4Feature = "ai.llm.gemma4";
    public const string Qwen35Feature = "ai.llm.qwen35";
    public const string Granite41Feature = "ai.llm.granite41";
    public const string Gpt56Feature = "ai.llm.gpt56";

    public static DigitalBrainModuleBuilder<AIModule> WithLlm<TModel>(this DigitalBrainModuleBuilder<AIModule> module)
        where TModel : class, ILLM
    {
        ArgumentNullException.ThrowIfNull(module);

        var state = module.Brain.GetOrAddState(static brain => new AIHostingState(brain), out var added);
        if (added)
        {
            module.RequireStateProtection();
            module.AddProjection(state);
        }

        module.ConfigureFeature(state.Add<TModel>());
        return module;
    }

    private sealed class AIHostingState(DigitalBrainBuilder brain) : DigitalBrainModuleProjection
    {
        private const string OllamaImageTag = "latest";

        private static readonly Dictionary<Type, (string ResourceSuffix, string Tag, string Feature)> OllamaModelCatalog = new()
        {
            [typeof(Llama32)] = ("llama32", "llama3.2", Llama32Feature),
            [typeof(Gemma4)] = ("gemma4", "gemma4:12b", Gemma4Feature),
            [typeof(Qwen35)] = ("qwen35", "qwen3.5:9b", Qwen35Feature),
            [typeof(Granite41)] = ("granite41", "granite4.1:8b", Granite41Feature),
        };

        private readonly HashSet<Type> _models = [];
        private readonly Dictionary<Type, IResourceBuilder<OllamaModelResource>> _ollamaModels = [];
        private IResourceBuilder<OllamaResource>? _ollama;
        private IResourceBuilder<OpenAIResource>? _openAI;
        private IResourceBuilder<OpenAIModelResource>? _gpt56;
        private IResourceBuilder<ParameterResource>? _openAIKey;

        internal string Add<TModel>()
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
                return ollama.Feature;
            }

            if (model == typeof(Gpt56))
            {
                AddGpt56();
                return Gpt56Feature;
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
                    .WithAnnotation(new WaitAnnotation(resource.Resource, WaitType.WaitUntilHealthy, exitCode: 0))
                    .WithEnvironment("DigitalBrain__AI__Ollama__Endpoint", resource.Resource.Parent.UriExpression)
                    .WithEnvironment($"DigitalBrain__AI__Ollama__{model.Name}__Model", resource.Resource.ModelName);
            }

            if (_gpt56 is not null)
            {
                builder
                    .WithEnvironment("DigitalBrain__AI__OpenAI__ApiKey", _openAIKey!)
                    .WithEnvironment("DigitalBrain__AI__OpenAI__Endpoint", _gpt56.Resource.Parent.UriExpression)
                    .WithEnvironment("DigitalBrain__AI__OpenAI__Gpt56__Model", _gpt56.Resource.Model);
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

            _ollamaModels[model] = _ollama.AddModel($"{brain.Name}-ai-{resourceSuffix}", tag);
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
