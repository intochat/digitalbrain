namespace DigitalBrain.Product.SalesInsights;

public sealed record SalesRevenueReadCompleted : Synapse
{
    public SalesRevenueReadCompleted(SalesQuery query, IReadOnlyList<SalesRevenueRecord> records)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(records);
        var copy = records.ToArray();
        if (copy.Any(static record => record is null))
        {
            throw new ArgumentException("Sales reader results cannot contain null records.", nameof(records));
        }

        Query = query;
        Records = Array.AsReadOnly(copy);
    }

    public SalesQuery Query { get; }

    public IReadOnlyList<SalesRevenueRecord> Records { get; }
}
