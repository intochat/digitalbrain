namespace DigitalBrain.Product.SalesInsights;

public sealed class SalesInsightState
{
    public SalesQuery? Query { get; set; }

    public SalesInsightContext? Context { get; set; }

    public bool Finalized { get; set; }

    public SalesInsightResult? Result { get; set; }

    public SalesInsightUnavailableReason? UnavailableReason { get; set; }
}
