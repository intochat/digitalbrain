namespace DigitalBrain.AI.OpenAI;

public sealed class TextEmbedding3Large : EmbeddingModel<ITextEmbedding3Large>
{
    public override string Id => "text-embedding-3-large";

    public override AiProvider Provider => AiProvider.OpenAI;

    public override int Dimensions => 3072;
}

public interface ITextEmbedding3Large : IEmbedding;
