namespace DigitalBrain.AI.Google;

public sealed class GeminiEmbedding : EmbeddingModel<IGeminiEmbedding>
{
    public override string Id => "gemini-embedding-001";

    public override AiProvider Provider => AiProvider.Google;

    public override int Dimensions => 3072;
}

public interface IGeminiEmbedding : IEmbedding;
