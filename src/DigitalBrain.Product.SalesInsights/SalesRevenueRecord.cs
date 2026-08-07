namespace DigitalBrain.Product.SalesInsights;

/// <summary>
/// One reader-returned closed-won amount in the query's reporting calendar.
/// </summary>
public sealed record SalesRevenueRecord
{
    public SalesRevenueRecord(DateOnly closedOn, decimal amount, string currencyCode)
    {
        if (closedOn == default)
        {
            throw new ArgumentOutOfRangeException(nameof(closedOn), "A sales record needs a reporting date.");
        }

        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "A closed-won sales amount cannot be negative.");
        }

        ClosedOn = closedOn;
        Amount = amount;
        CurrencyCode = SalesQuery.NormalizeCurrency(currencyCode, nameof(currencyCode));
    }

    public DateOnly ClosedOn { get; }

    public decimal Amount { get; }

    public string CurrencyCode { get; }
}
