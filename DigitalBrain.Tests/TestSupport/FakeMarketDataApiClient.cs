using DigitalBrain.Kernel.Market;

namespace DigitalBrain.Tests.TestSupport;

// Deterministic fake: returns a settable price with zero external I/O. Price is mutable so Reqnroll
// Given-steps can change it after the silo/DI container is already built (ConfigureSilo runs once at
// cluster startup, before any scenario steps execute).
public sealed class FakeMarketDataApiClient : IMarketDataApiClient
{
    public string Price { get; set; } = "$0.00";

    public Task<string> GetBitcoinPriceUsdAsync(CancellationToken ct = default) => Task.FromResult(Price);
}
