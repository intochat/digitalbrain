using System.Text.Json.Serialization;

namespace DigitalBrain.Catalog;

[GenerateSerializer]
[Alias("db.catalog.source-position")]
public readonly record struct CatalogSourcePosition : IComparable<CatalogSourcePosition>
{
    [JsonConstructor]
    public CatalogSourcePosition(long epoch, long sequence)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(epoch);
        ArgumentOutOfRangeException.ThrowIfNegative(sequence);
        if (sequence == 0 && epoch != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sequence),
                "Sequence zero is reserved for the pre-history origin.");
        }

        Epoch = epoch;
        Sequence = sequence;
    }

    [Id(0)] public long Epoch { get; }
    [Id(1)] public long Sequence { get; }

    public static CatalogSourcePosition Origin { get; } = new(0, 0);
    public static CatalogSourcePosition First { get; } = new(0, 1);
    [JsonIgnore]
    public bool IsOrigin => this == Origin;

    public int CompareTo(CatalogSourcePosition other)
    {
        var epoch = Epoch.CompareTo(other.Epoch);
        return epoch == 0 ? Sequence.CompareTo(other.Sequence) : epoch;
    }

    public bool IsImmediateSuccessorOf(CatalogSourcePosition previous)
        => previous.IsOrigin
            ? this == First
            : Epoch == previous.Epoch && Sequence == previous.Sequence + 1 ||
            Epoch == previous.Epoch + 1 && Sequence == 1;
}

[GenerateSerializer]
[Alias("db.catalog.source-partition")]
public sealed record CatalogSourcePartition
{
    [JsonConstructor]
    public CatalogSourcePartition(string sourceKind, string partitionId, CatalogScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        SourceKind = CatalogContractValidation.Required(sourceKind, nameof(sourceKind));
        PartitionId = CatalogContractValidation.Required(partitionId, nameof(partitionId));
        scope.Validate();
        Scope = scope;
    }

    [Id(0)] public string SourceKind { get; }
    [Id(1)] public string PartitionId { get; }
    [Id(2)] public CatalogScope Scope { get; }

    public void Validate() => _ = new CatalogSourcePartition(SourceKind, PartitionId, Scope);
}

[GenerateSerializer]
[Alias("db.catalog.source-snapshot")]
public sealed record CatalogSourceSnapshot
{
    [JsonConstructor]
    public CatalogSourceSnapshot(
        CatalogSourcePartition partition,
        string snapshotToken,
        CatalogSourcePosition highWatermark)
    {
        ArgumentNullException.ThrowIfNull(partition);
        partition.Validate();
        Partition = partition;
        SnapshotToken = CatalogContractValidation.OpaqueRequired(snapshotToken, nameof(snapshotToken));
        HighWatermark = highWatermark;
    }

    [Id(0)] public CatalogSourcePartition Partition { get; }
    [Id(1)] public string SnapshotToken { get; }
    [Id(2)] public CatalogSourcePosition HighWatermark { get; }
}

[GenerateSerializer]
[Alias("db.catalog.source-snapshot-item")]
public sealed record CatalogSourceSnapshotItem
{
    [JsonConstructor]
    public CatalogSourceSnapshotItem(CatalogSourcePosition position, CatalogDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (position.IsOrigin)
        {
            throw new ArgumentException("The pre-history origin cannot identify a snapshot item.", nameof(position));
        }

        descriptor.Validate();
        Position = position;
        Descriptor = descriptor;
    }

    [Id(0)] public CatalogSourcePosition Position { get; }
    [Id(1)] public CatalogDescriptor Descriptor { get; }
}

[GenerateSerializer]
[Alias("db.catalog.source-snapshot-page")]
public sealed record CatalogSourceSnapshotPage
{
    [JsonConstructor]
    public CatalogSourceSnapshotPage(
        string snapshotToken,
        CatalogSourcePosition highWatermark,
        IReadOnlyList<CatalogSourceSnapshotItem>? items,
        string? continuationToken)
    {
        SnapshotToken = CatalogContractValidation.OpaqueRequired(snapshotToken, nameof(snapshotToken));
        HighWatermark = highWatermark;
        var copiedItems = CatalogContractValidation.ReadOnlyCopy(items);
        if (copiedItems.Any(item => item is null || item.Position.CompareTo(highWatermark) > 0))
        {
            throw new ArgumentException(
                "Snapshot items must be non-null and cannot exceed the page high watermark.",
                nameof(items));
        }

        Items = copiedItems;
        ContinuationToken = CatalogContractValidation.OpaqueOptional(continuationToken, nameof(continuationToken));
    }

    [Id(0)] public string SnapshotToken { get; }
    [Id(1)] public CatalogSourcePosition HighWatermark { get; }
    [Id(2)] public IReadOnlyList<CatalogSourceSnapshotItem> Items { get; }
    [Id(3)] public string? ContinuationToken { get; }
}

[GenerateSerializer]
[Alias("db.catalog.mutation-kind")]
public enum CatalogMutationKind
{
    Upsert = 0,
    Tombstone = 1,
}

[GenerateSerializer]
[Alias("db.catalog.mutation")]
public sealed record CatalogMutation
{
    [JsonConstructor]
    public CatalogMutation(
        Guid mutationId,
        CatalogSourcePartition partition,
        CatalogSourcePosition position,
        CatalogReference reference,
        CatalogMutationKind kind,
        CatalogDescriptor? descriptor)
    {
        if (mutationId == Guid.Empty)
        {
            throw new ArgumentException("A mutation id is required.", nameof(mutationId));
        }

        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(reference);
        if (position.IsOrigin)
        {
            throw new ArgumentException("The pre-history origin cannot identify a mutation.", nameof(position));
        }

        partition.Validate();
        reference.Validate();
        if (!string.Equals(partition.SourceKind, reference.Source.Kind, StringComparison.Ordinal) ||
            partition.Scope != reference.Scope)
        {
            throw new ArgumentException("Mutation partition and catalog reference must have the same source and scope.");
        }

        if (kind == CatalogMutationKind.Upsert)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            descriptor.Validate();
            if (descriptor.Reference != reference)
            {
                throw new ArgumentException("An upsert descriptor must match its exact mutation reference.");
            }
        }
        else if (kind == CatalogMutationKind.Tombstone)
        {
            if (descriptor is not null)
            {
                throw new ArgumentException("A tombstone cannot carry a descriptor.", nameof(descriptor));
            }
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        MutationId = mutationId;
        Partition = partition;
        Position = position;
        Reference = reference;
        Kind = kind;
        Descriptor = descriptor;
    }

    [Id(0)] public Guid MutationId { get; }
    [Id(1)] public CatalogSourcePartition Partition { get; }
    [Id(2)] public CatalogSourcePosition Position { get; }
    [Id(3)] public CatalogReference Reference { get; }
    [Id(4)] public CatalogMutationKind Kind { get; }
    [Id(5)] public CatalogDescriptor? Descriptor { get; }

    public static CatalogMutation Upsert(
        Guid mutationId,
        CatalogSourcePartition partition,
        CatalogDescriptor descriptor,
        CatalogSourcePosition position)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return new(mutationId, partition, position, descriptor.Reference, CatalogMutationKind.Upsert, descriptor);
    }

    public static CatalogMutation Tombstone(
        Guid mutationId,
        CatalogSourcePartition partition,
        CatalogReference reference,
        CatalogSourcePosition position)
        => new(mutationId, partition, position, reference, CatalogMutationKind.Tombstone, null);
}

public sealed class CatalogSourceSnapshotRequiredException : Exception
{
    public CatalogSourceSnapshotRequiredException()
        : this("The catalog source snapshot must be restarted.")
    {
    }

    public CatalogSourceSnapshotRequiredException(string message)
        : base(message)
    {
    }

    public CatalogSourceSnapshotRequiredException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
