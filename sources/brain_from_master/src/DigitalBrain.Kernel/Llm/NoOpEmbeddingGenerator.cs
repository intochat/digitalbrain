using Microsoft.Extensions.AI;
namespace DigitalBrain.Kernel.Llm;

internal sealed class NoOpEmbeddingGenerator(int dimensions = 384) : IEmbeddingGenerator<string, Embedding<float>>
{
    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var embeddings = values.Select(_ => new Embedding<float>(new float[dimensions])).ToList();
        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
    }
    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }
}
