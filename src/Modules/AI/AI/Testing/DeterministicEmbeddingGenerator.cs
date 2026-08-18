using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

// Testing-mode stand-in for a real embedding model: a 64-dimension token-bag hash,
// L2-normalized. Identical text always embeds identically and texts sharing tokens share
// buckets; DETERMINISM IS THE ONLY GUARANTEE — the geometry carries no semantic meaning.
internal sealed class DeterministicEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private const int Dimensions = 64;
    private const uint FnvOffsetBasis = 2166136261;
    private const uint FnvPrime = 16777619;

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(
            values.Select(static value => new Embedding<float>(Embed(value)))));
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose()
    {
    }

    private static float[] Embed(string text)
    {
        var vector = new float[Dimensions];
        var tokenHash = FnvOffsetBasis;
        var insideToken = false;
        foreach (var character in text)
        {
            if (char.IsLetterOrDigit(character))
            {
                // FNV-1a over the lowercased token, character by character. A stable hash is
                // load-bearing: string.GetHashCode is randomized per process and would embed
                // the same text differently across hosts.
                tokenHash = (tokenHash ^ char.ToLowerInvariant(character)) * FnvPrime;
                insideToken = true;
                continue;
            }

            if (insideToken)
            {
                vector[tokenHash % Dimensions]++;
                tokenHash = FnvOffsetBasis;
                insideToken = false;
            }
        }

        if (insideToken)
        {
            vector[tokenHash % Dimensions]++;
        }

        var length = MathF.Sqrt(vector.Sum(static component => component * component));
        if (length > 0)
        {
            for (var bucket = 0; bucket < vector.Length; bucket++)
            {
                vector[bucket] /= length;
            }
        }

        return vector;
    }
}
