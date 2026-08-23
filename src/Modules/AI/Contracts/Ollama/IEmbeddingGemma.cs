namespace DigitalBrain.AI.Ollama;

public sealed class EmbeddingGemma : EmbeddingModel<IEmbeddingGemma>
{
    public override string Id => "embeddinggemma";

    public override LlmProvider Provider => LlmProvider.Ollama;
}

public interface IEmbeddingGemma : IEmbedding;
