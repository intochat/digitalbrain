namespace DigitalBrain.AI.Google;

public sealed class GeminiEmbedding : EmbeddingModel<IGeminiEmbedding>
{
    public override string Id => "gemini-embedding-001";

    public override LlmProvider Provider => LlmProvider.Google;
}

public interface IGeminiEmbedding : IEmbedding;
