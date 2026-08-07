namespace DigitalBrain.Product.SalesInsights;

public sealed record SalesInsightUnavailable : Synapse
{
    public SalesInsightUnavailable(
        string queryId,
        SalesInsightContext context,
        SalesInsightUnavailableReason reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queryId);
        QueryId = queryId.Trim();
        Context = context ?? throw new ArgumentNullException(nameof(context));
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "The sales insight unavailable reason is not recognized.");
        }

        Reason = reason;
    }

    public string QueryId { get; }

    public SalesInsightContext Context { get; }

    public SalesInsightUnavailableReason Reason { get; }
}
