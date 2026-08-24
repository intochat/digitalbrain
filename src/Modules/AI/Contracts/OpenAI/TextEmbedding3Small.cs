namespace DigitalBrain.AI.OpenAI;

public sealed class TextEmbedding3Small : EmbeddingModel<ITextEmbedding3Small>
{
    public override string Id => "text-embedding-3-small";

    public override AiProvider Provider => AiProvider.OpenAI;
}

public interface ITextEmbedding3Small : IEmbedding;
