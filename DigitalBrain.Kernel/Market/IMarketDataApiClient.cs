namespace DigitalBrain.Kernel.Market;

public interface IMarketDataApiClient
{
    Task<string> GetBitcoinPriceUsdAsync(CancellationToken ct = default);
}
