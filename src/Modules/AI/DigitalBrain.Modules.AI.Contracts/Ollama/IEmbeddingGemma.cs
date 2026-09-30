namespace DigitalBrain.AI.Ollama;

public sealed class EmbeddingGemma : EmbeddingModel<IEmbeddingGemma>
{
    public override string Id => "embeddinggemma";

    public override AiProvider Provider => AiProvider.Ollama;

    public override int Dimensions => 768;
}

public interface IEmbeddingGemma : IEmbedding;
