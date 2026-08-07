namespace DigitalBrain.Product.Memory;

public interface IMemoryStore
{
    /// <summary>
    /// Creates or replaces one entry by its immutable <see cref="MemoryEntry.Id"/>.
    /// Repeating the same entry id must converge on one logical stored entry.
    /// </summary>
    Task<MemoryStoreResult> StoreAsync(MemoryEntry entry, CancellationToken cancellationToken);

    Task<IReadOnlyList<MemoryHit>> SearchAsync(MemoryQuery query, CancellationToken cancellationToken);

    Task RemoveAsync(string entryId, CancellationToken cancellationToken);
}
