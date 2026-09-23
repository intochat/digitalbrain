using System.Text;

namespace DigitalBrain.Discovery.Search;

// Deterministic, dependency-free embedding used when no model is available and by the gate test.
internal sealed class HashingCapabilityEmbedder : ICapabilityEmbedder
{
    public const int Dimensions = 256;
    public const string ModelName = "hashing-v1";

    public string ModelId => ModelName;

    public ValueTask<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var vector = new float[Dimensions];
        foreach (var token in TextTokens.Split(text))
        {
            var hash = Fnv1a(token);
            var bucket = (int)(hash % Dimensions);
            vector[bucket] += (hash & 1) == 0 ? 1f : -1f;
        }

        Normalize(vector);
        return ValueTask.FromResult(vector);
    }

    internal static void Normalize(float[] vector)
    {
        var sum = 0d;
        foreach (var value in vector)
        {
            sum += value * value;
        }

        if (sum <= 0)
        {
            return;
        }

        var length = (float)Math.Sqrt(sum);
        for (var index = 0; index < vector.Length; index++)
        {
            vector[index] /= length;
        }
    }

    internal static float Cosine(float[] left, float[] right)
    {
        if (left.Length != right.Length)
        {
            return 0f;
        }

        var dot = 0f;
        for (var index = 0; index < left.Length; index++)
        {
            dot += left[index] * right[index];
        }

        return dot;
    }

    private static uint Fnv1a(string value)
    {
        var hash = 2166136261u;
        foreach (var character in Encoding.UTF8.GetBytes(value))
        {
            hash ^= character;
            hash *= 16777619u;
        }

        return hash;
    }
}
