namespace DigitalBrain.Marketplace;

// IntoChat is the merchant of record: it collects tax on the full price a customer paid, keeps a
// take-rate fee on the price net of tax, and passes the remainder to the developer as fiat net.
internal static class MerchantOfRecord
{
    internal static (decimal FeeUsd, decimal NetUsd) Split(decimal grossUsd, decimal taxUsd, decimal takeRate)
    {
        var netOfTax = grossUsd - taxUsd;
        var fee = Round(netOfTax * takeRate);
        return (fee, netOfTax - fee);
    }

    internal static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
