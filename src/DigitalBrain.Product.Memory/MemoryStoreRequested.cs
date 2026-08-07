namespace DigitalBrain.Product.Memory;

public sealed record MemoryStoreRequested : Synapse
{
    public MemoryStoreRequested(MemoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Entry = entry;
    }

    public MemoryEntry Entry { get; }
}
