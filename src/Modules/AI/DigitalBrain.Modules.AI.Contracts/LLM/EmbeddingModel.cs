namespace DigitalBrain.AI;

public abstract class EmbeddingModel : AiModel
{
    // Abstract on purpose: a stored collection is keyed to one width, so
    // swapping the default embedding for one of a different width orphans every
    // vector already written. Stating it makes that mismatch checkable instead
    // of a comment in the composition root.
    public abstract int Dimensions { get; }

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