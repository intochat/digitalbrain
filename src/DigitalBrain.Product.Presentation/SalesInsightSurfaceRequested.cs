using DigitalBrain.Product.SalesInsights;

namespace DigitalBrain.Product.Presentation;

/// <summary>
/// A renderer-neutral daily sales chart/table declaration. It deliberately
/// carries aggregates rather than provider rows, queries, or actions.
/// </summary>
public sealed record SalesInsightSurfaceRequested : Synapse
{
    public SalesInsightSurfaceRequested(
        string queryId,
        SalesDateRange range,
        string currencyCode,
        IReadOnlyList<SalesRevenueBucket> buckets,
        decimal totalAmount,
        int closedDealCount,
        SalesInsightContext context,
        IReadOnlyList<SalesInsightDisplay> displays,
        IReadOnlyList<SalesInsightPlacement> placements)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queryId);
        ArgumentNullException.ThrowIfNull(range);
        ArgumentException.ThrowIfNullOrWhiteSpace(currencyCode);
        ArgumentNullException.ThrowIfNull(buckets);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(displays);
        ArgumentNullException.ThrowIfNull(placements);
        if (totalAmount < 0 || closedDealCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalAmount), "A sales surface total and count cannot be negative.");
        }

        var bucketCopy = buckets.ToArray();
        var displayCopy = displays.ToArray();
        var placementCopy = placements.ToArray();
        if (bucketCopy.Length == 0 || bucketCopy.Any(static bucket => bucket is null))
        {
            throw new ArgumentException("A sales surface needs non-null daily buckets.", nameof(buckets));
        }

        if (displayCopy.Length == 0 || displayCopy.Any(static display => !Enum.IsDefined(display)))
        {
            throw new ArgumentException("A sales surface needs recognized display hints.", nameof(displays));
        }

        if (placementCopy.Length == 0 || placementCopy.Any(static placement => !Enum.IsDefined(placement)))
        {
            throw new ArgumentException("A sales surface needs recognized placement hints.", nameof(placements));
        }

        QueryId = queryId.Trim();
        Range = range;
        CurrencyCode = NormalizeCurrency(currencyCode);
        Buckets = Array.AsReadOnly(bucketCopy);
        TotalAmount = totalAmount;
        ClosedDealCount = closedDealCount;
        Context = context;
        Displays = Array.AsReadOnly(displayCopy);
        Placements = Array.AsReadOnly(placementCopy);
    }

    public string QueryId { get; }

    public SalesDateRange Range { get; }

    public string CurrencyCode { get; }

    public IReadOnlyList<SalesRevenueBucket> Buckets { get; }

    public decimal TotalAmount { get; }

    public int ClosedDealCount { get; }

    public SalesInsightContext Context { get; }

    public IReadOnlyList<SalesInsightDisplay> Displays { get; }

    public IReadOnlyList<SalesInsightPlacement> Placements { get; }

    private static string NormalizeCurrency(string currencyCode)
    {
        var normalized = currencyCode.Trim().ToUpperInvariant();
        if (normalized.Length != 3 || normalized.Any(static character => !char.IsAsciiLetter(character)))
        {
            throw new ArgumentException("A currency code must have three ASCII letters.", nameof(currencyCode));
        }

        return normalized;
    }
}
