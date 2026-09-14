using Azure;
using Azure.Data.Tables;
using DigitalBrain.Abstractions.Slots;

namespace DigitalBrain.Aspire;

// One row in DigitalBrainLeases: which slot may react, and when it last changed. The table is ours, not
// Orleans' (spike S3: the clustering table's layout is Orleans' private schema).
internal sealed class ActiveSlotLeaseEntity : ITableEntity
{
    public string PartitionKey { get; set; } = ActiveSlotNames.PartitionKey;

    public string RowKey { get; set; } = ActiveSlotNames.RowKey;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public string Owner { get; set; } = string.Empty;

    public long Generation { get; set; }

    // Audit-only: stamped on every ownership change. Nothing reads it back to judge liveness.
    public DateTimeOffset ChangedAt { get; set; }
}
