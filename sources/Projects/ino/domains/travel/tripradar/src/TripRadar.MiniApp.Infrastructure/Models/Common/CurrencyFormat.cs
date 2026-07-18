using System.Globalization;

namespace TripRadar.MiniApp.Client.Infrastructure.Models.Common;

public static class CurrencyFormat
{
    private const string DefaultCurrency = "USD";

    public static string FormatPrice(decimal price, string? currency)
    {
        var normalizedCurrency = string.IsNullOrWhiteSpace(currency)
            ? DefaultCurrency
            : currency.Trim().ToUpperInvariant();
        var amount = price.ToString("0", CultureInfo.InvariantCulture);
        return $"{normalizedCurrency} {amount}";
    }
}