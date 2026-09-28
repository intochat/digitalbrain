using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.AI.FoundryLocal;
using DigitalBrain.Aspire.Hosting;

namespace DigitalBrain.AI.Aspire.Hosting;

public static partial class AIHostingExtensions
{
    private const string TavilyApiKeyEnvironmentKey = "DigitalBrain__AI__Tavily__ApiKey";
    private const string TavilyEnabledEnvironmentKey = "DigitalBrain__AI__Tavily__Enabled";
    private const string TavilyApiKeyDescription = "API key for Tavily web search. Sign up at [Tavily](https://www.tavily.com/) and copy your API key from your account dashboard. Paste it here to enable web search for DigitalBrain agents.";
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

    // Speech-to-text, local or hosted. ITranscription constrains the marker, so a
    // marker of the wrong kind is a compile error rather than a runtime lookup miss.
    public static DigitalBrainModuleBuilder<AIModule> WithVoiceToText<TModel>(
        this DigitalBrainModuleBuilder<AIModule> module)
        where TModel : ITranscription
    {
        ArgumentNullException.ThrowIfNull(module);
        var marker = typeof(TModel);
        var whisper = TranscriptionModel.FindByMarker(marker)
            ?? throw new NotSupportedException(
                $"{marker.FullName} is not a catalogued transcription model. "
                + $"Known models: {string.Join(", ", TranscriptionModel.All.Select(static m => m.Marker.Name))}.");

        var voice = module.DigitalBrainBuilder.GetOrAddState(brain => new VoiceToTextHostingState(brain, module.Resource), out var added);
        if (added)
        {
            module.AddProjection(voice);
        }

        // A hosted transcription model needs its provider key like any other model.
        // WithLlm/WithEmbedding get this through AddModel; voice has its own
        // projection, so without this an AppHost that pins a hosted model without
        // also registering a chat model of the same provider ships no key, and the
        // endpoint answers 503 forever with no host-side way to fix it.
        if (!whisper.IsLocal)
        {
            State(module).EnsureProviderApiKey(whisper.Provider);
        }

        voice.SetModel(whisper);
        return module;
    }

    public static DigitalBrainModuleBuilder<AIModule> WithTavilySearch(
        this DigitalBrainModuleBuilder<AIModule> module)
    {
        ArgumentNullException.ThrowIfNull(module);
        State(module).EnableTavilySearch();
        return module;
    }

    private static AIHostingState State(DigitalBrainModuleBuilder<AIModule> module)
    {
        ArgumentNullException.ThrowIfNull(module);
        var state = module.DigitalBrainBuilder.GetOrAddState(brain => new AIHostingState(brain, module.Resource), out var added);
        if (added)
        {
            module.AddProjection(state);
        }

        return state;
    }

}