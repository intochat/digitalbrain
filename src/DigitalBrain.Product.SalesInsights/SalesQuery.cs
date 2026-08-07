namespace DigitalBrain.Product.SalesInsights;

/// <summary>
/// A correlated closed-won revenue query in one currency.
/// </summary>
public sealed record SalesQuery
{
    public SalesQuery(string queryId, SalesDateRange range, string currencyCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queryId);
        ArgumentNullException.ThrowIfNull(range);

        QueryId = queryId.Trim();
        Range = range;
        CurrencyCode = NormalizeCurrency(currencyCode, nameof(currencyCode));
    }

    public string QueryId { get; }

    public SalesDateRange Range { get; }

    public string CurrencyCode { get; }

    internal static string NormalizeCurrency(string currencyCode, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currencyCode);
        var normalized = currencyCode.Trim().ToUpperInvariant();
        if (normalized.Length != 3 || normalized.Any(static character => !char.IsAsciiLetter(character)))
        {
            throw new ArgumentException("A currency code must have three ASCII letters.", parameterName);
        }

        return normalized;
    }
}
