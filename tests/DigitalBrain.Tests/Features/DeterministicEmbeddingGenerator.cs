using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Tests;

internal sealed class DeterministicEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        GeneratedEmbeddings<Embedding<float>> embeddings = [];
        foreach (var text in values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
            var vector = Array.ConvertAll(hash, value => value - 127.5f);
            var norm = MathF.Sqrt(vector.Sum(value => value * value));
            for (var index = 0; index < vector.Length; index++)
            {
                vector[index] /= norm;
            }

            embeddings.Add(new Embedding<float>(vector));
        }

        return Task.FromResult(embeddings);
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose()
    {
    }
}
