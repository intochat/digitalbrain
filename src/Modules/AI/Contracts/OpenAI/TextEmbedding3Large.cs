namespace DigitalBrain.AI.OpenAI;

public sealed class TextEmbedding3Large : EmbeddingModel<ITextEmbedding3Large>
{
    public override string Id => "text-embedding-3-large";

    public override LlmProvider Provider => LlmProvider.OpenAI;
}

public interface ITextEmbedding3Large : IEmbedding;
