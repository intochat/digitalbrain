namespace DigitalBrain.Compute.Storage;

// Shadow storage meter. It keeps the highest byte count seen for each UTC day — the daily
// peak — and reports the running daily-peak average as storage.gb_month. Billing uses the
// average-of-daily-peaks rule; nothing here charges, the event is shadow only.
internal sealed class StorageSampler(IStorageUsageProbe probe, Metering.IMeterStore store)
{
    internal const string MeterId = "storage.gb_month";
    private const decimal BytesPerGigabyte = 1024m * 1024m * 1024m;
    private const decimal DaysPerMonth = 30.44m;

    private readonly Dictionary<DateOnly, long> dailyPeaks = [];

    public decimal AverageDailyPeakGigabytes => dailyPeaks.Count == 0
        ? 0m
        : dailyPeaks.Values.Sum(bytes => bytes / BytesPerGigabyte) / dailyPeaks.Count;

    public decimal GigabyteMonths => AverageDailyPeakGigabytes * (dailyPeaks.Count / DaysPerMonth);

    public async ValueTask<MeterEvent?> SampleAsync(string workspaceId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        var day = DateOnly.FromDateTime(now.UtcDateTime);
        var bytes = await probe.ReadBytesAsync(cancellationToken).ConfigureAwait(false);
        if (dailyPeaks.TryGetValue(day, out var peak) && bytes <= peak) { return null; }
        dailyPeaks[day] = bytes;

        var meterEvent = new MeterEvent
        {
            IntentId = $"storage:{workspaceId}:{day:yyyy-MM}",
            MeterId = MeterId,
            Step = day.ToString("yyyy-MM-dd"),
            WorkspaceId = workspaceId,
            Quantity = GigabyteMonths,
            Unit = "gb_month",
            Source = MeterSource.StorageSampler,
            CostBasis = "shadow",
            OccurredAt = now,
        };
        await store.AppendAsync(meterEvent, cancellationToken).ConfigureAwait(false);
        return meterEvent;
    }
}
