namespace DigitalBrain.AI;

public static class AIOptionsExtensions
{
    public static AIOptions WithLlm<TModel>(this AIOptions options) where TModel : ILLM
    {
        var model = LLMModel.FindByMarker(typeof(TModel)) ?? throw new ArgumentException("Unknown LLM marker.");
        if (!options.Hosting.Llms.Contains(model.Marker.Name)) { options.Hosting.Llms.Add(model.Marker.Name); }
        return options;
    }

    public static AIOptions WithDefaultLlm<TModel>(this AIOptions options) where TModel : ILLM
    {
        options.WithLlm<TModel>();
        options.Default.Model = typeof(TModel).Name;
        return options;
    }

    public static AIOptions WithEmbedding<TModel>(this AIOptions options) where TModel : IEmbedding
    {
        var model = EmbeddingModel.FindByMarker(typeof(TModel)) ?? throw new ArgumentException("Unknown embedding marker.");
        if (!options.Hosting.Embeddings.Contains(model.Marker.Name)) { options.Hosting.Embeddings.Add(model.Marker.Name); }
        return options;
    }

    public static AIOptions WithDefaultEmbedding<TModel>(this AIOptions options) where TModel : IEmbedding
    {
        options.WithEmbedding<TModel>();
        options.Default.Embedding = typeof(TModel).Name;
        return options;
    }

    public static AIOptions WithVoiceToText<TModel>(this AIOptions options) where TModel : ITranscription
    {
        var model = TranscriptionModel.FindByMarker(typeof(TModel)) ?? throw new ArgumentException("Unknown transcription marker.");
        options.Default.Transcription = model.Marker.Name;
        return options;
    }

    public static AIOptions WithoutVoiceToText(this AIOptions options)
    {
        options.Default.Transcription = null;
        return options;
    }

    public static AIOptions WithModelEndpoint(this AIOptions options, AiProvider provider, Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme is not ("http" or "https"))
        { throw new ArgumentException("A model endpoint must be an absolute HTTP(S) URI.", nameof(endpoint)); }
        options.Provider(provider).Endpoint = endpoint.OriginalString;
        return options;
    }

    public static AIOptions WithTavilySearch(this AIOptions options)
    {
        options.Tavily.Enabled = true;
        return options;
    }

    public static AIOptions WithoutWebSearch(this AIOptions options)
    {
        options.Tavily.Enabled = false;
        return options;
    }

    public static AIOptions WithoutLocalModels(this AIOptions options)
    {
        options.Hosting.Llms.RemoveAll(name => LLMModel.FindByMarkerName(name)?.IsLocal == true);
        options.Hosting.Embeddings.RemoveAll(name => EmbeddingModel.FindByMarkerName(name)?.IsLocal == true);
        if (options.Default.Model is { } llm && LLMModel.FindByMarkerName(llm)?.IsLocal == true) { options.Default.Model = null; }
        if (options.Default.Embedding is { } embedding && EmbeddingModel.FindByMarkerName(embedding)?.IsLocal == true) { options.Default.Embedding = null; }
        return options;
    }
}
