namespace DigitalBrain.Product.SalesInsights;

/// <summary>
/// A safe daily aggregate suitable for a renderer-neutral surface.
/// </summary>
public sealed record SalesRevenueBucket
{
    public SalesRevenueBucket(DateOnly date, decimal amount, int closedDealCount)
    {
        if (date == default)
        {
            throw new ArgumentOutOfRangeException(nameof(date), "A sales bucket needs a date.");
        }

        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "A sales bucket amount cannot be negative.");
        }

        if (closedDealCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(closedDealCount), "A sales bucket count cannot be negative.");
        }

        Date = date;
        Amount = amount;
        ClosedDealCount = closedDealCount;
    }

    public DateOnly Date { get; }

    public decimal Amount { get; }

    public int ClosedDealCount { get; }
}
