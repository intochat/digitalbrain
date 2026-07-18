using Microsoft.Extensions.AI;

namespace DigitalBrain.SDK.DigitalBrain.Ai.Embedding;

internal sealed class StubEmbeddingGenerator(int dimensions = 8)
    : IEmbeddingGenerator<string, Embedding<float>>
{
    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var result = new GeneratedEmbeddings<Embedding<float>>();
        foreach (var value in values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var vector = new float[dimensions];
            var seed = StringComparer.Ordinal.GetHashCode(value ?? string.Empty);
            for (var i = 0; i < dimensions; i++)
                vector[i] = ((seed ^ i) & 0xFF) / 255f;
            result.Add(new Embedding<float>(vector));
        }
        return Task.FromResult(result);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() { }
}
