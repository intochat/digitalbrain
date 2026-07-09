namespace DigitalBrain.Core.Models.OpenAI;

public sealed class TextEmbedding3Small : EmbeddingModel
{
    public override string Provider => DigitalBrainProviderIds.OpenAI;
    public override string Id => "text-embedding-3-small";
    public override string DisplayName => "text-embedding-3-small";
}
