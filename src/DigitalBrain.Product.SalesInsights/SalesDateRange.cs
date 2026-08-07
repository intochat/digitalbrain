namespace DigitalBrain.Product.SalesInsights;

/// <summary>
/// An explicit half-open reporting range. Relative phrases are resolved before
/// they enter the durable product graph.
/// </summary>
public sealed record SalesDateRange
{
    public const int MaximumDays = 366;

    public SalesDateRange(DateOnly fromInclusive, DateOnly toExclusive)
    {
        if (fromInclusive == default || toExclusive == default || fromInclusive >= toExclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(toExclusive), "A sales range must have a non-empty explicit interval.");
        }

        if (toExclusive.DayNumber - fromInclusive.DayNumber > MaximumDays)
        {
            throw new ArgumentOutOfRangeException(
                nameof(toExclusive),
                $"A sales range cannot exceed {MaximumDays} calendar days.");
        }

        FromInclusive = fromInclusive;
        ToExclusive = toExclusive;
    }

    public DateOnly FromInclusive { get; }

    public DateOnly ToExclusive { get; }

    public bool Contains(DateOnly date)
        => date >= FromInclusive && date < ToExclusive;
}
