namespace DigitalBrain.Product.SalesInsights;

public sealed record SalesInsightRequested : Synapse
{
    public SalesInsightRequested(SalesQuery query, SalesInsightContext context)
    {
        Query = query ?? throw new ArgumentNullException(nameof(query));
        Context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public SalesQuery Query { get; }

    public SalesInsightContext Context { get; }
}
