using DigitalBrain.Aspire.Hosting;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.AI.Aspire.Hosting;

public sealed class AIModuleHosting : IDigitalBrainModuleHosting
{
    public void Configure(DigitalBrainBuilder brain)
        => AIHostingExtensions.ConfigureDeclaration(brain,
            brain.GetModuleConfiguration<AIModule>().GetSection(AIOptions.SectionName).Get<AIOptions>() ?? new());
}

public static partial class AIHostingExtensions
{
    internal static void ConfigureDeclaration(DigitalBrainBuilder brain, AIOptions options)
    {
        var module = new DigitalBrainModuleBuilder<AIModule>(brain);
        var state = State(module);
        state.EnableSensitiveData = options.Telemetry.EnableSensitiveData ?? false;
        foreach (var name in options.Hosting.Llms)
        {
            var model = LLMModel.FindByMarkerName(name) ?? throw new InvalidOperationException("Unknown LLM marker.");
            state.AddLlm(model.Marker);
        }
        foreach (var name in options.Hosting.Embeddings)
        {
            var model = EmbeddingModel.FindByMarkerName(name) ?? throw new InvalidOperationException("Unknown embedding marker.");
            state.AddEmbedding(model.Marker);
        }
        if (options.Default.Transcription is { Length: > 0 } nameOfVoice)
        {
            var model = TranscriptionModel.FindByMarkerName(nameOfVoice) ?? throw new InvalidOperationException("Unknown transcription marker.");
            var voice = brain.GetOrAddState(b => new VoiceToTextHostingState(b, module.Resource), out var added);
            if (added) { module.AddProjection(voice); }
            if (!model.IsLocal) { state.EnsureProviderApiKey(model.Provider); }
            voice.SetModel(model);
        }
        if (options.Tavily.Enabled) { state.EnableTavilySearch(); }
    }
}