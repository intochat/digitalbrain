namespace DigitalBrain.Core.Models.GitHub;

public sealed class TextEmbedding3Small : EmbeddingModel
{
    public override string Provider => DigitalBrainProviderIds.GitHubModels;
    public override string Id => "openai/text-embedding-3-small";
    public override string DisplayName => "text-embedding-3-small";
}
