namespace DigitalBrain.Product.SalesInsights;

public sealed record SalesRevenueReadUnavailable : Synapse
{
    public SalesRevenueReadUnavailable(string queryId, SalesInsightUnavailableReason reason)
    {
        QueryId = ValidateQueryId(queryId);
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "The sales reader unavailable reason is not recognized.");
        }

        Reason = reason;
    }

    public string QueryId { get; }

    public SalesInsightUnavailableReason Reason { get; }

    private static string ValidateQueryId(string queryId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queryId);
        return queryId.Trim();
    }
}
