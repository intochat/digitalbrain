using Azure;
using Azure.Data.Tables;
using DigitalBrain.Abstractions.Slots;

namespace DigitalBrain.Aspire;

// One row in DigitalBrainLeases: which slot may react, and when it last said so. The table is ours, not
// Orleans' (spike S3: the clustering table's layout is Orleans' private schema).
public sealed class ActiveSlotLeaseEntity : ITableEntity
{
    public string PartitionKey { get; set; } = ActiveSlotNames.PartitionKey;

    public string RowKey { get; set; } = ActiveSlotNames.RowKey;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public string Owner { get; set; } = string.Empty;

    public long Generation { get; set; }

    public DateTimeOffset Fenced { get; set; }
}
