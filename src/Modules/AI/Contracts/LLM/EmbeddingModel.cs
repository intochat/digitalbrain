namespace DigitalBrain.AI;

public abstract class EmbeddingModel
{
    public abstract string Id { get; }

    public abstract LlmProvider Provider { get; }

    public abstract Type Marker { get; }

    public bool IsLocal => Provider == LlmProvider.Ollama;

    public static IReadOnlyList<EmbeddingModel> All { get; } =
    [
        new OpenAI.TextEmbedding3Small(),
        new OpenAI.TextEmbedding3Large(),
        new Google.GeminiEmbedding(),
        new Ollama.EmbeddingGemma(),
    ];

    public static EmbeddingModel? FindByMarker(Type marker)
        => All.FirstOrDefault(model => model.Marker == marker);

    public static EmbeddingModel? FindByMarkerName(string markerName)
        => All.FirstOrDefault(model => string.Equals(model.Marker.Name, markerName, StringComparison.Ordinal));
}

public abstract class EmbeddingModel<TMarker> : EmbeddingModel
    where TMarker : IEmbedding
{
    public sealed override Type Marker => typeof(TMarker);
}
