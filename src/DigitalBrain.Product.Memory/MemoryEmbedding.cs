using System.Collections.ObjectModel;

namespace DigitalBrain.Product.Memory;

/// <summary>
/// An immutable finite vector returned by an <see cref="ITextEmbeddingGenerator"/>.
/// </summary>
public sealed record MemoryEmbedding
{
    public MemoryEmbedding(IReadOnlyList<float> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            throw new ArgumentException("A memory embedding needs at least one dimension.", nameof(values));
        }

        var copy = new float[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            if (!float.IsFinite(values[index]))
            {
                throw new ArgumentOutOfRangeException(nameof(values), "Memory embedding values must be finite.");
            }

            copy[index] = values[index];
        }

        Values = new ReadOnlyCollection<float>(copy);
    }

    public IReadOnlyList<float> Values { get; }
}
