namespace DigitalBrain.Product.Memory;

public sealed record MemoryStoreResult
{
    public MemoryStoreResult(string entryId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        EntryId = entryId.Trim();
    }

    public string EntryId { get; }
}
