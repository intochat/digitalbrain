namespace DigitalBrain.Product.SalesInsights;

public sealed record SalesRevenueReadRequested : Synapse
{
    public SalesRevenueReadRequested(SalesQuery query)
    {
        Query = query ?? throw new ArgumentNullException(nameof(query));
    }

    public SalesQuery Query { get; }
}
