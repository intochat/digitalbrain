using Microsoft.Extensions.AI;

namespace DigitalBrain.OS.Bdd.Tests;

internal sealed class ScriptedEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var embeddings = values
            .Select(static value => new Embedding<float>(Embed(value)))
            .ToList();
        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }

    private static float[] Embed(string text)
    {
        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var vector = new float[8];
        foreach (var token in tokens)
        {
            var hash = StringComparer.OrdinalIgnoreCase.GetHashCode(token);
            var index = (int)((uint)hash % vector.Length);
            vector[index] += 1f + (hash & 0xFF) / 255f;
        }

        var magnitude = MathF.Sqrt(vector.Sum(static value => value * value));
        if (magnitude > 0f)
        {
            for (var i = 0; i < vector.Length; i++)
            {
                vector[i] /= magnitude;
            }
        }

        return vector;
    }
}
