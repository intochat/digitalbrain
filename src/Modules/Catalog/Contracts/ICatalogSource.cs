namespace DigitalBrain.Catalog;

public interface ICatalogSource
{
    string SourceKind { get; }

    IAsyncEnumerable<CatalogSourcePartition> EnumeratePartitionsAsync(
        CancellationToken cancellationToken);

    Task<CatalogSourceSnapshot> BeginSnapshotAsync(
        CatalogSourcePartition partition,
        CancellationToken cancellationToken);

    Task<CatalogSourceSnapshotPage> ReadSnapshotPageAsync(
        CatalogSourceSnapshot snapshot,
        string? continuationToken,
        int pageSize,
        CancellationToken cancellationToken);

    Task<CatalogSourcePosition> ReadCurrentPositionAsync(
        CatalogSourcePartition partition,
        CancellationToken cancellationToken);

    IAsyncEnumerable<CatalogMutation> ReadMutationsAsync(
        CatalogSourcePartition partition,
        CatalogSourcePosition afterExclusive,
        CatalogSourcePosition throughInclusive,
        CancellationToken cancellationToken);

    Task<CatalogDescriptor?> ResolveExactAsync(
        CatalogReference reference,
        CancellationToken cancellationToken);

    Task<CatalogDescriptor?> ResolveCurrentAsync(
        CatalogScope scope,
        CatalogSourceReference source,
        CatalogEntryId id,
        CancellationToken cancellationToken);
}
