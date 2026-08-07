namespace DigitalBrain.Product.Memory;

/// <summary>
/// Produces a stable embedding for text used by a memory storage provider.
/// Embeddings are an infrastructure concern: they never travel in memory facts
/// or across the <see cref="IMemoryStore"/> boundary.
/// </summary>
public interface ITextEmbeddingGenerator
{
    Task<MemoryEmbedding> EmbedAsync(string text, CancellationToken cancellationToken);
}
