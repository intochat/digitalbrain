namespace DigitalBrain.Product.Memory;

public sealed record MemorySearchCompleted : Synapse
{
    public MemorySearchCompleted(MemoryQuery query, IReadOnlyList<MemoryHit> hits)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(hits);

        var copy = hits.ToArray();
        if (copy.Any(static hit => hit is null))
        {
            throw new ArgumentException("Memory search hits cannot contain null entries.", nameof(hits));
        }

        Query = query;
        Hits = Array.AsReadOnly(copy);
    }

    public MemoryQuery Query { get; }

    public IReadOnlyList<MemoryHit> Hits { get; }
}
