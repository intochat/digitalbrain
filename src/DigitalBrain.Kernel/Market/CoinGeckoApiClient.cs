using System.Text.Json;

namespace DigitalBrain.Kernel.Market;

public sealed class CoinGeckoApiClient(HttpClient httpClient) : IMarketDataApiClient
{
    private const string PriceUrl = "https://api.coingecko.com/api/v3/simple/price?ids=bitcoin&vs_currencies=usd";

    public async Task<string> GetBitcoinPriceUsdAsync(CancellationToken ct = default)
    {
        using var response = await httpClient.GetAsync(PriceUrl, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var usd = doc.RootElement.GetProperty("bitcoin").GetProperty("usd").GetDecimal();
        return "$" + usd.ToString("N2", System.Globalization.CultureInfo.InvariantCulture);
    }
}
