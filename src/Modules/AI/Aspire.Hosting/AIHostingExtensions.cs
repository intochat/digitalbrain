using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.AI.FoundryLocal;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Aspire.Hosting;

namespace DigitalBrain.AI.Aspire.Hosting;

public static class AIHostingExtensions
{
    private const string EnableSensitiveDataEnvironmentKey =
        "DigitalBrain__AI__Telemetry__EnableSensitiveData";

    public const string Gemma4Feature = "ai.llm.gemma4";

    extension(DigitalBrainModuleBuilder<AIModule> module)
    {
        public bool EnableSensitiveData
        {
            get => State(module).EnableSensitiveData;
            set => State(module).EnableSensitiveData = value;
        }
    }

    public static DigitalBrainModuleBuilder<AIModule> WithLlm<TModel>(this DigitalBrainModuleBuilder<AIModule> module)
        where TModel : class
    {
        State(module).Add<TModel>();
        return module;
    }

    public static DigitalBrainModuleBuilder<AIModule> WithEmbedding<TModel>(this DigitalBrainModuleBuilder<AIModule> module)
        where TModel : class
    {
        State(module).Add<TModel>();
        return module;
    }

    // Local Whisper STT (Foundry Local). Marker types live in DigitalBrain.AI.FoundryLocal.
    public static DigitalBrainModuleBuilder<AIModule> WithVoiceToText<TModel>(
        this DigitalBrainModuleBuilder<AIModule> module)
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(module);
        var marker = typeof(TModel);
        var whisper = WhisperModel.FindByMarker(marker)
            ?? throw new NotSupportedException(
                $"{marker.FullName} is not a known Whisper model marker. "
                + "Use IWhisperTiny, IWhisperSmall, or IWhisperLargeV3Turbo.");

        var voice = module.Brain.GetOrAddState(static brain => new VoiceToTextHostingState(brain), out var added);
        if (added)
        {
            module.AddProjection(voice);
        }

        voice.SetModel(whisper);
        return module;
    }

    private static AIHostingState State(DigitalBrainModuleBuilder<AIModule> module)
    {
        ArgumentNullException.ThrowIfNull(module);
        var state = module.Brain.GetOrAddState(static brain => new AIHostingState(brain), out var added);
        if (added)
        {
            module.AddProjection(state);
        }

        return state;
    }

    private sealed class AIHostingState(DigitalBrainBuilder brain) : DigitalBrainModuleProjection
    {
        private const string OllamaImageTag = "latest";

        private static readonly (string ResourceName, string Tag) Gemma4Model = ("gemma4-12b", "gemma4:12b");
        private static readonly (string ResourceName, string Tag) EmbeddingGemmaModel = ("embeddinggemma", "embeddinggemma");

        private readonly HashSet<Type> _models = [];
        private readonly Dictionary<Type, IResourceBuilder<OllamaModelResource>> _ollamaModels = [];
        private IResourceBuilder<OllamaResource>? _ollama;

        internal bool EnableSensitiveData { get; set; }

        internal string Add<TModel>()
            where TModel : class
        {
            var model = typeof(TModel);

            if (!_models.Add(model))
            {
                throw new InvalidOperationException(
                    $"{model.FullName} is already configured on brain '{brain.Name}'. Add each model exactly once.");
            }

            if (model == typeof(DigitalBrain.AI.Ollama.IGemma4))
            {
                AddOllamaModel(model, Gemma4Model.ResourceName, Gemma4Model.Tag);
                return Gemma4Feature;
            }

            if (model == typeof(DigitalBrain.AI.Ollama.IEmbeddingGemma))
            {
                AddOllamaModel(model, EmbeddingGemmaModel.ResourceName, EmbeddingGemmaModel.Tag);
                return "ai.embedding.embeddinggemma";
            }

            throw new NotSupportedException(
                $"{model.FullName} is not a supported product AI model. Use {nameof(DigitalBrain.AI.Ollama.IGemma4)} or {nameof(DigitalBrain.AI.Ollama.IEmbeddingGemma)}.");
        }

        public override void Apply<TResource>(IResourceBuilder<TResource> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.WithEnvironment(
                EnableSensitiveDataEnvironmentKey,
                EnableSensitiveData.ToString());

            foreach (var (model, resource) in _ollamaModels)
            {
                builder
                    .WithAnnotation(new WaitAnnotation(resource.Resource, WaitType.WaitUntilHealthy, exitCode: 0))
                    .WithEnvironment("DigitalBrain__AI__Ollama__Endpoint", resource.Resource.Parent.UriExpression)
                    .WithEnvironment($"DigitalBrain__AI__Ollama__{model.Name}__Model", resource.Resource.ModelName);
            }
        }

        private IResourceBuilder<OllamaResource> EnsureOllama()
            => _ollama ??= brain.ApplicationBuilder
                .AddOllama("ollama")
                .WithImageTag(OllamaImageTag)
                .WithGPUSupport()
                .WithDataVolume()
                .WithLifetime(ContainerLifetime.Persistent)
                .WithEnvironment("OLLAMA_KEEP_ALIVE", "-1")
                .WithParentRelationship(brain.Resource);

        private void AddOllamaModel(Type model, string resourceName, string tag)
            => _ollamaModels[model] = EnsureOllama().AddModel(resourceName, tag);

    }

    private sealed class VoiceToTextHostingState(DigitalBrainBuilder brain) : DigitalBrainModuleProjection
    {
        private const string ModelIdEnvironmentKey = "DigitalBrain__AI__Whisper__ModelId";
        private WhisperModel? _model;

        internal void SetModel(WhisperModel model)
        {
            ArgumentNullException.ThrowIfNull(model);
            if (_model is not null && !string.Equals(_model.Id, model.Id, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Voice-to-text is already configured on brain '{brain.Name}' as '{_model.Id}'. "
                    + "Call WithVoiceToText once.");
            }

            _model = model;
        }

        public override void Apply<TResource>(IResourceBuilder<TResource> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            if (_model is null)
            {
                return;
            }

            builder.WithEnvironment(ModelIdEnvironmentKey, _model.Id);
        }
    }
}
