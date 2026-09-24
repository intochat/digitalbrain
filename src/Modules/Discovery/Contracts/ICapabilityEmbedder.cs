namespace DigitalBrain.Discovery;

// Embeddings are computed off the grain call and every collection records the model that produced them.
public interface ICapabilityEmbedder
{
    string ModelId { get; }

    ValueTask<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);
}
