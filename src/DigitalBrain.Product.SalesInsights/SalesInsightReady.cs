namespace DigitalBrain.Product.SalesInsights;

public sealed record SalesInsightReady : Synapse
{
    public SalesInsightReady(SalesInsightResult result)
    {
        Result = result ?? throw new ArgumentNullException(nameof(result));
    }

    public SalesInsightResult Result { get; }
}
