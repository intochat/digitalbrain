using DigitalBrain.Compute.Metering;

namespace DigitalBrain.Compute.Billing;

// Rolls a month's charges and recurring meters into one statement. A recurring meter has no user
// intent: it accrues for the period on its own and still appears as its own line. Pure so the
// statement can be tested without a grain.
internal static class StatementBuilder
{
    internal static MonthlyStatement Build(
        string accountId,
        string period,
        IReadOnlyList<LedgerEntry> charges,
        IReadOnlyList<MeterEvent> meters,
        IPriceBook priceBook,
        DateTimeOffset generatedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(period);
        ArgumentNullException.ThrowIfNull(charges);
        ArgumentNullException.ThrowIfNull(meters);
        ArgumentNullException.ThrowIfNull(priceBook);

        var periodStart = ParsePeriod(period);
        var periodEnd = periodStart.AddMonths(1);
        var lines = new List<StatementLine>();

        lines.AddRange(charges
            .Where(charge => InPeriod(charge.OccurredAt, periodStart, periodEnd))
            .GroupBy(charge => (charge.AppId, charge.Description))
            .Select(group => new StatementLine
            {
                AppId = group.Key.AppId,
                Operation = group.Key.Description ?? "call",
                Compute = group.Sum(charge => charge.Amount),
                Count = group.Count(),
                Recurring = false,
            }));

        lines.AddRange(meters
            .Where(meter => Recurring(meter) && InPeriod(meter.OccurredAt, periodStart, periodEnd))
            .GroupBy(meter => (meter.AppId, meter.MeterId))
            .Select(group => new StatementLine
            {
                AppId = group.Key.AppId,
                Operation = group.Key.MeterId,
                Compute = group.Sum(meter => priceBook.PriceInCompute(meter.MeterId, meter.Quantity)),
                Count = group.Count(),
                Recurring = true,
                MeterId = group.Key.MeterId,
            }));

        var total = lines.Sum(line => line.Compute);
        return new MonthlyStatement
        {
            AccountId = accountId,
            Period = period,
            Lines = lines,
            TotalCompute = total,
            TotalUsd = ComputeUnits.ToUsd(total),
            GeneratedAt = generatedAt,
        };
    }

    internal static bool Recurring(MeterEvent meter)
        => string.Equals(meter.CostBasis, RecurringMeters.CostBasis, StringComparison.Ordinal)
            || meter.Source == MeterSource.StorageSampler;

    private static DateTimeOffset ParsePeriod(string period)
        => DateTimeOffset.ParseExact(period, "yyyy-MM", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal);

    private static bool InPeriod(DateTimeOffset moment, DateTimeOffset start, DateTimeOffset end)
        => moment >= start && moment < end;
}
