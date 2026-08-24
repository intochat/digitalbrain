using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.AI.FoundryLocal;
using DigitalBrain.Aspire.Hosting;

namespace DigitalBrain.AI.Aspire.Hosting;

public static class AIHostingExtensions
{
    private const string EnableSensitiveDataEnvironmentKey =
        "DigitalBrain__AI__Telemetry__EnableSensitiveData";

    extension(DigitalBrainModuleBuilder<AIModule> module)
    {
        public bool EnableSensitiveData
        {
            get => State(module).EnableSensitiveData;
            set => State(module).EnableSensitiveData = value;
        }
    }

    public static DigitalBrainModuleBuilder<AIModule> WithLlm<TModel>(this DigitalBrainModuleBuilder<AIModule> module)
        where TModel : ILLM
    {
        ArgumentNullException.ThrowIfNull(module);
        State(module).AddLlm(typeof(TModel));
        return module;
    }

    public static DigitalBrainModuleBuilder<AIModule> WithEmbedding<TModel>(this DigitalBrainModuleBuilder<AIModule> module)
        where TModel : IEmbedding
    {
        ArgumentNullException.ThrowIfNull(module);
        State(module).AddEmbedding(typeof(TModel));
        return module;
    }

    public static DigitalBrainModuleBuilder<AIModule> WithDefaultLlm<TModel>(this DigitalBrainModuleBuilder<AIModule> module)
        where TModel : ILLM
    {
        ArgumentNullException.ThrowIfNull(module);
        State(module).SetDefaultLlm(typeof(TModel));
        return module;
    }

    public static DigitalBrainModuleBuilder<AIModule> WithDefaultEmbedding<TModel>(this DigitalBrainModuleBuilder<AIModule> module)
        where TModel : IEmbedding
    {
        ArgumentNullException.ThrowIfNull(module);
        State(module).SetDefaultEmbedding(typeof(TModel));
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

        private readonly HashSet<Type> _markers = [];
        private readonly Dictionary<Type, IResourceBuilder<OllamaModelResource>> _ollamaModels = [];
        private readonly Dictionary<AiProvider, IResourceBuilder<ParameterResource>> _providerApiKeys = [];
        private IResourceBuilder<OllamaResource>? _ollama;
        private Type? _defaultLlmMarker;
        private Type? _defaultEmbeddingMarker;

        internal bool EnableSensitiveData { get; set; }

        internal void AddLlm(Type marker)
        {
            var model = LLMModel.FindByMarker(marker)
                ?? throw new NotSupportedException(
                    $"{marker.FullName} is not a known LLM model marker. Add it to LLMModel.All first.");

            AddModel(marker, model.Provider, model.Id);
        }

        internal void AddEmbedding(Type marker)
        {
            var model = EmbeddingModel.FindByMarker(marker)
                ?? throw new NotSupportedException(
                    $"{marker.FullName} is not a known embedding model marker. Add it to EmbeddingModel.All first.");

            AddModel(marker, model.Provider, model.Id);
        }

        internal void SetDefaultLlm(Type marker)
        {
            RequireAdded(marker);
            _defaultLlmMarker = marker;
        }

        internal void SetDefaultEmbedding(Type marker)
        {
            RequireAdded(marker);
            _defaultEmbeddingMarker = marker;
        }

        public override void Apply<TResource>(IResourceBuilder<TResource> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.WithEnvironment(
                EnableSensitiveDataEnvironmentKey,
                EnableSensitiveData.ToString());

            foreach (var (marker, resource) in _ollamaModels)
            {
                builder
                    .WithAnnotation(new WaitAnnotation(resource.Resource, WaitType.WaitUntilHealthy, exitCode: 0))
                    .WithEnvironment("DigitalBrain__AI__Ollama__Endpoint", resource.Resource.Parent.UriExpression)
                    .WithEnvironment($"DigitalBrain__AI__Ollama__{marker.Name}__Model", resource.Resource.ModelName);
            }

            foreach (var (provider, apiKey) in _providerApiKeys)
            {
                builder.WithEnvironment($"DigitalBrain__AI__{provider}__ApiKey", apiKey);
            }

            if (_defaultLlmMarker is { } llmMarker)
            {
                builder.WithEnvironment("DigitalBrain__AI__Default__Model", llmMarker.Name);
            }

            if (_defaultEmbeddingMarker is { } embeddingMarker)
            {
                builder.WithEnvironment("DigitalBrain__AI__Default__Embedding", embeddingMarker.Name);
            }
        }

        private void AddModel(Type marker, AiProvider provider, string id)
        {
            if (!_markers.Add(marker))
            {
                throw new InvalidOperationException(
                    $"{marker.FullName} is already configured on brain '{brain.Name}'. Add each model exactly once.");
            }

            if (provider == AiProvider.Ollama)
            {
                _ollamaModels[marker] = EnsureOllama().AddModel(OllamaResourceName(id), id);
            }
            else
            {
                EnsureProviderApiKey(provider);
            }
        }

        private void RequireAdded(Type marker)
        {
            if (!_markers.Contains(marker))
            {
                throw new InvalidOperationException(
                    $"{marker.Name} must be added with WithLlm/WithEmbedding before it can become the default.");
            }
        }

        private void EnsureProviderApiKey(AiProvider provider)
        {
            if (_providerApiKeys.ContainsKey(provider))
            {
                return;
            }

            // Empty default keeps boot and test hosts unblocked; real values come
            // from user secrets in dev and Key Vault-injected parameters in prod.
            _providerApiKeys[provider] = brain.ApplicationBuilder.AddParameter(
                $"{provider.ToString().ToLowerInvariant()}-api-key",
                string.Empty,
                publishValueAsDefault: false,
                secret: true);
        }

        private static string OllamaResourceName(string id)
            => id.ToLowerInvariant().Replace(':', '-').Replace('.', '-').Replace('/', '-');

        private IResourceBuilder<OllamaResource> EnsureOllama()
            => _ollama ??= brain.ApplicationBuilder
                .AddOllama("ollama")
                .WithImageTag(OllamaImageTag)
                .WithGPUSupport()
                .WithDataVolume()
                .WithLifetime(ContainerLifetime.Persistent)
                .WithEnvironment("OLLAMA_KEEP_ALIVE", "-1")
                .WithParentRelationship(brain.Resource);
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
