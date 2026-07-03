using DigitalBrain.Core;

namespace DigitalBrain.Kernel.Market;

public interface IMarketDataNeuron : INeuron, IHandle<Signal> { }

[GrainType("digitalbrain.market-data")]
public sealed class MarketDataNeuron(ILogger<MarketDataNeuron> logger, NeuronJournals journals, IMarketDataApiClient client)
    : Neuron(logger, journals), IMarketDataNeuron
{
    public async Task HandleAsync(Signal signal)
    {
        if (signal.Name != "CheckBitcoinPrice") return;

        var price = await client.GetBitcoinPriceUsdAsync();
        var props = new Dictionary<string, object?>(signal.Props) { ["price"] = price };
        await Broadcast(new Signal("BitcoinPriceChecked", props));
    }
}
