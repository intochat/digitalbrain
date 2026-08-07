using System.Collections.ObjectModel;

namespace DigitalBrain.Product.Memory;

public sealed record MemoryEntry
{
    public MemoryEntry(string id, string content, IReadOnlyDictionary<string, string> metadata)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        ArgumentNullException.ThrowIfNull(metadata);

        Id = id.Trim();
        Content = content.Trim();
        Metadata = CopyMetadata(metadata, nameof(metadata));
    }

    public string Id { get; }

    public string Content { get; }

    public IReadOnlyDictionary<string, string> Metadata { get; }

    internal static IReadOnlyDictionary<string, string> CopyMetadata(
        IReadOnlyDictionary<string, string> metadata,
        string parameterName)
    {
        var copy = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in metadata)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Memory metadata keys cannot be blank.", parameterName);
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Memory metadata values cannot be blank.", parameterName);
            }

            if (!copy.TryAdd(key.Trim(), value.Trim()))
            {
                throw new ArgumentException("Memory metadata keys must be unique after trimming.", parameterName);
            }
        }

        return new ReadOnlyDictionary<string, string>(copy);
    }
}
