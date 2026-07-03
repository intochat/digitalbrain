using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Market;
using DigitalBrain.Tests.TestSupport;
using DigitalBrain.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;

namespace DigitalBrain.Tests.Market;

// Emitter grain that broadcasts an arbitrary named Signal so the neuron under test can receive it
// from the timeline (same pattern as AskLlmEmitter in DigitalBrain.Tests/Kernel/LlmResponderTests.cs).
public interface ISignalEmitter : INeuron
{
    Task BroadcastSignalAsync(string name, IReadOnlyDictionary<string, object?> props);
}

public sealed class SignalEmitter(Microsoft.Extensions.Logging.ILogger<SignalEmitter> logger, NeuronJournals journals)
    : Neuron(logger, journals), ISignalEmitter
{
    public Task BroadcastSignalAsync(string name, IReadOnlyDictionary<string, object?> props) =>
        Broadcast(new Signal(name, props));
}

public sealed class MarketDataNeuronTests : NeuronTestBase
{
    private readonly FakeMarketDataApiClient _fakeClient = new() { Price = "$61,234.56" };

    protected override void ConfigureSilo(ISiloBuilder builder) =>
        builder.ConfigureServices(services => services.AddSingleton<IMarketDataApiClient>(_fakeClient));

    [Fact]
    public async Task CheckBitcoinPrice_signal_triggers_BitcoinPriceChecked_reply_with_price_and_chatId()
    {
        // Activate the neuron so it subscribes to the timeline before the broadcast arrives.
        var marketData = Grain<IMarketDataNeuron>("market-data-1");
        await marketData.GetTimelineAsync();

        var emitter = Grain<ISignalEmitter>("emitter-1");
        await emitter.BroadcastSignalAsync("CheckBitcoinPrice", new Dictionary<string, object?> { ["chatId"] = 7L });

        Signal? signal = null;
        for (var attempt = 0; attempt < 20 && signal is null; attempt++)
        {
            await Task.Delay(50);
            var timeline = await marketData.GetTimelineAsync();
            signal = timeline.OfType<Signal>().FirstOrDefault(s => s.Name == "BitcoinPriceChecked");
        }

        Assert.NotNull(signal);
        Assert.Equal(7L, signal!.Props["chatId"]);
        Assert.Equal("$61,234.56", signal.Props["price"]);
    }
}
