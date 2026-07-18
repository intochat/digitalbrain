namespace TripRadar.Server.Domain.ReferenceData;

public class Currency
{
    private Currency()
    {
    }

    public Currency(string currencyCode, string currencyName)
    {
        CurrencyCode = currencyCode.ToUpperInvariant();
        CurrencyName = currencyName.Trim();
    }

    public string CurrencyCode { get; } = null!;

    public string CurrencyName { get; } = null!;
}
