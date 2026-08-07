namespace DigitalBrain.Product.Memory;

public sealed record MemoryQuery
{
    public MemoryQuery(string text, int maximumResults, IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (maximumResults <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumResults), "A memory query needs at least one result.");
        }

        Text = text.Trim();
        MaximumResults = maximumResults;
        Metadata = MemoryEntry.CopyMetadata(metadata ?? EmptyMetadata, nameof(metadata));
    }

    public string Text { get; }

    public int MaximumResults { get; }

    public IReadOnlyDictionary<string, string> Metadata { get; }

    private static IReadOnlyDictionary<string, string> EmptyMetadata { get; }
        = new Dictionary<string, string>(StringComparer.Ordinal);
}
