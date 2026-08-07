namespace DigitalBrain.Product.SalesInsights;

/// <summary>
/// The frozen product result of one completed sales query.
/// </summary>
public sealed record SalesInsightResult
{
    public SalesInsightResult(
        SalesQuery query,
        SalesInsightContext context,
        IReadOnlyList<SalesRevenueBucket> buckets,
        decimal totalAmount,
        int closedDealCount)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(buckets);
        var copy = buckets.ToArray();
        if (copy.Length == 0 || copy.Any(static bucket => bucket is null))
        {
            throw new ArgumentException("A completed sales insight needs non-null daily buckets.", nameof(buckets));
        }

        if (totalAmount < 0 || closedDealCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalAmount), "A sales insight total and count cannot be negative.");
        }

        Query = query;
        Context = context;
        Buckets = Array.AsReadOnly(copy);
        TotalAmount = totalAmount;
        ClosedDealCount = closedDealCount;
    }

    public SalesQuery Query { get; }

    public SalesInsightContext Context { get; }

    public IReadOnlyList<SalesRevenueBucket> Buckets { get; }

    public decimal TotalAmount { get; }

    public int ClosedDealCount { get; }
}
