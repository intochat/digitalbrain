using DigitalBrain.Core;

namespace DigitalBrain.AI;

public sealed class AIConfigurationContract() : ModuleConfigurationContract<AIModule, AIOptions>(
    "Default.Profile", "Default.Provider", "Default.Model", "Default.Reasoning", "Default.MaxOutputTokens",
    "Default.Capabilities", "Default.Embedding", "Default.Transcription", "Default.Image",
    "Telemetry.EnableSensitiveData", "OpenAI.Endpoint", "Anthropic.Endpoint", "Google.Endpoint", "XAI.Endpoint",
    "Ollama.Endpoint", "Ollama.Models", "Tavily.Enabled", "ModelProfiles", "Hosting.Llms", "Hosting.Embeddings")
{
    protected override ModuleDefinition Compile(AIOptions options) => AIModule.Define(options);
    protected override void Validate(AIOptions options)
    {
        if (options.OpenAI.ApiKey is not null || options.Anthropic.ApiKey is not null
            || options.Google.ApiKey is not null || options.XAI.ApiKey is not null
            || options.Ollama.ApiKey is not null || options.Tavily.ApiKey is not null)
        {
            throw new ArgumentException("API keys cannot be supplied in module declarations. Use private configuration or Aspire secret parameters instead.", nameof(options));
        }
    }
}

public static class AIModuleConfiguration
{
    public static ModuleConfiguration<AIModule> WithOptions(this ModuleConfiguration<AIModule> module, AIOptions options)
    {
        module.ReplaceOptions(options);
        return module;
    }
    public static ModuleConfiguration<AIModule> WithLlm<TModel>(this ModuleConfiguration<AIModule> module) where TModel : ILLM
    {
        var model = LLMModel.FindByMarker(typeof(TModel)) ?? throw new ArgumentException("Unknown LLM marker.");
        module.ConfigureOptions<AIOptions>(o => { if (!o.Hosting.Llms.Contains(model.Marker.Name)) { o.Hosting.Llms.Add(model.Marker.Name); } }, "Hosting.Llms");
        return module;
    }
    public static ModuleConfiguration<AIModule> WithDefaultLlm<TModel>(this ModuleConfiguration<AIModule> module) where TModel : ILLM
    {
        module.WithLlm<TModel>();
        module.ConfigureOptions<AIOptions>(o => o.Default.Model = typeof(TModel).Name, "Default.Model");
        return module;
    }
    public static ModuleConfiguration<AIModule> WithEmbedding<TModel>(this ModuleConfiguration<AIModule> module) where TModel : IEmbedding
    {
        var model = EmbeddingModel.FindByMarker(typeof(TModel)) ?? throw new ArgumentException("Unknown embedding marker.");
        module.ConfigureOptions<AIOptions>(o => { if (!o.Hosting.Embeddings.Contains(model.Marker.Name)) { o.Hosting.Embeddings.Add(model.Marker.Name); } }, "Hosting.Embeddings");
        return module;
    }
    public static ModuleConfiguration<AIModule> WithDefaultEmbedding<TModel>(this ModuleConfiguration<AIModule> module) where TModel : IEmbedding
    {
        module.WithEmbedding<TModel>();
        module.ConfigureOptions<AIOptions>(o => o.Default.Embedding = typeof(TModel).Name, "Default.Embedding");
        return module;
    }
    public static ModuleConfiguration<AIModule> WithVoiceToText<TModel>(this ModuleConfiguration<AIModule> module) where TModel : ITranscription
    {
        var model = TranscriptionModel.FindByMarker(typeof(TModel)) ?? throw new ArgumentException("Unknown transcription marker.");
        module.ConfigureOptions<AIOptions>(o => o.Default.Transcription = model.Marker.Name, "Default.Transcription");
        return module;
    }
    public static ModuleConfiguration<AIModule> WithoutVoiceToText(this ModuleConfiguration<AIModule> module)
    {
        module.ConfigureOptions<AIOptions>(o => o.Default.Transcription = null, "Default.Transcription");
        return module;
    }
    public static ModuleConfiguration<AIModule> WithModelEndpoint(this ModuleConfiguration<AIModule> module, AiProvider provider, Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme is not ("http" or "https"))
            { throw new ArgumentException("A model endpoint must be an absolute HTTP(S) URI.", nameof(endpoint)); }
        module.ConfigureOptions<AIOptions>(o => o.Provider(provider).Endpoint = endpoint.OriginalString, $"{provider}.Endpoint");
        return module;
    }
    public static ModuleConfiguration<AIModule> WithTavilySearch(this ModuleConfiguration<AIModule> module)
        => WebSearch(module, true);
    public static ModuleConfiguration<AIModule> WithoutWebSearch(this ModuleConfiguration<AIModule> module)
        => WebSearch(module, false);
    private static ModuleConfiguration<AIModule> WebSearch(ModuleConfiguration<AIModule> module, bool enabled)
    {
        module.ConfigureOptions<AIOptions>(o => o.Tavily.Enabled = enabled, "Tavily.Enabled");
        return module;
    }
    public static ModuleConfiguration<AIModule> WithoutLocalModels(this ModuleConfiguration<AIModule> module)
    {
        module.ConfigureOptions<AIOptions>(o =>
        {
            o.Hosting.Llms.RemoveAll(name => LLMModel.FindByMarkerName(name)?.IsLocal == true);
            o.Hosting.Embeddings.RemoveAll(name => EmbeddingModel.FindByMarkerName(name)?.IsLocal == true);
            if (o.Default.Model is { } llm && LLMModel.FindByMarkerName(llm)?.IsLocal == true) { o.Default.Model = null; }
            if (o.Default.Embedding is { } embedding && EmbeddingModel.FindByMarkerName(embedding)?.IsLocal == true) { o.Default.Embedding = null; }
        }, "Hosting.Llms", "Hosting.Embeddings", "Default.Model", "Default.Embedding");
        return module;
    }
}
