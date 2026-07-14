namespace DigitalBrain.Kernel.Contracts.Models.Ollama;

public sealed class MxbaiEmbedLarge : EmbeddingModel
{
    public override string Provider => DigitalBrainProviderIds.Ollama;
    public override string Id => "mxbai-embed-large";
    public override string DisplayName => "mxbai-embed-large";
}
