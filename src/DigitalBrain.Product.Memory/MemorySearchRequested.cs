namespace DigitalBrain.Product.Memory;

public sealed record MemorySearchRequested : Synapse
{
    public MemorySearchRequested(MemoryQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        Query = query;
    }

    public MemoryQuery Query { get; }
}
