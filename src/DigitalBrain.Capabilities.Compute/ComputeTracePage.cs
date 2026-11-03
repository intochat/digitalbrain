namespace DigitalBrain.Capabilities.Compute;

public enum TraceDetailStatus
{
    Complete = 0,
    Incomplete = 1,
    Expired = 2,
    DetailUnavailable = 3,
}

public sealed record ComputeTracePage
{
    public Cost[] Rows { get; init; } = [];
    public long ChargedMicros { get; init; }
    public long KnownListPriceMicros { get; init; }
    public long UnpricedCount { get; init; }
    public long PendingCount { get; init; }
    public long UnknownCount { get; init; }
    public long SnapshotWatermark { get; init; }
    public string? ContinuationToken { get; init; }
    public TraceDetailStatus Status { get; init; }
}
