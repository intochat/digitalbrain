namespace DigitalBrain.Compute.Metering;

// A recurring meter accrues for a period without a user intent: storage and subscriptions are
// billed from the meter alone. The synthetic intent id keys one accrual per meter and period.
internal static class RecurringMeters
{
    internal const string CostBasis = "recurring";

    internal static MeterEvent Accrue(
        string workspaceId,
        string meterId,
        decimal quantity,
        string unit,
        DateTimeOffset now,
        string? appId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(meterId);
        var period = now.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        return new MeterEvent
        {
            IntentId = $"recurring:{meterId}:{period}",
            MeterId = meterId,
            Step = period,
            WorkspaceId = workspaceId,
            Quantity = quantity,
            Unit = unit,
            Source = MeterSource.StorageSampler,
            AppId = appId,
            CostBasis = CostBasis,
            OccurredAt = now,
        };
    }
}
