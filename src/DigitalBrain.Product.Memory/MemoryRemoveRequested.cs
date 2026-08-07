namespace DigitalBrain.Product.Memory;

public sealed record MemoryRemoveRequested : Synapse
{
    public MemoryRemoveRequested(string entryId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        EntryId = entryId.Trim();
    }

    public string EntryId { get; }
}
