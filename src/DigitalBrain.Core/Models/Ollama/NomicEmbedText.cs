namespace DigitalBrain.Core.Models.Ollama;

public sealed class NomicEmbedText : EmbeddingModel
{
    public override string Provider => DigitalBrainProviderIds.Ollama;
    public override string Id => "nomic-embed-text";
}
