namespace DigitalBrain.AI.Ollama;

public sealed class EmbeddingGemma : EmbeddingModel<IEmbeddingGemma>
{
    public override string Id => "embeddinggemma";

    public override AiProvider Provider => AiProvider.Ollama;
}

public interface IEmbeddingGemma : IEmbedding;
