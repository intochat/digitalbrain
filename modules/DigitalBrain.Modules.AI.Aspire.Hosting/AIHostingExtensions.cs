using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.OpenAI;
using DigitalBrain.AI;
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
        private readonly HashSet<Type> _models = [];
        private IResourceBuilder<OllamaResource>? _ollama;
        private IResourceBuilder<OllamaModelResource>? _llama32;
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

            if (model == typeof(Llama32))
            {
                AddLlama32();
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

            if (_llama32 is not null)
            {
                builder
                    .WithAnnotation(new WaitAnnotation(
                        _llama32.Resource,
                        WaitType.WaitUntilHealthy,
                        exitCode: 0))
                    .WithEnvironment(
                        "DigitalBrain__AI__Ollama__Endpoint",
                        _llama32.Resource.Parent.UriExpression)
                    .WithEnvironment(
                        "DigitalBrain__AI__Ollama__Llama32__Model",
                        _llama32.Resource.ModelName);
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

        private void AddLlama32()
        {
            var builder = brain.GetApplicationBuilder();
            _ollama ??= builder
                .AddOllama($"{brain.Name}-ai-ollama")
                .WithDataVolume();
            _llama32 = _ollama.AddModel($"{brain.Name}-ai-llama32", "llama3.2");
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
