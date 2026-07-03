using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Market;
using DigitalBrain.Mcp.Tools;
using DigitalBrain.Tests.TestSupport;
using DigitalBrain.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using Reqnroll;

namespace DigitalBrain.Tests.Steps;

// End-to-end proof of the full X -> Bitcoin -> Telegram loop over a real TestCluster, market data
// stubbed: simulate_x_post -> IngressNeuron broadcast -> embodied demo pack -> Signal("CheckBitcoinPrice")
// -> MarketDataNeuron (fake IMarketDataApiClient) -> Signal("BitcoinPriceChecked") -> embodied demo pack
// -> Signal("TelegramReplyRequested") -> egress bus. Scoped to "xbitcoindemo" for the same reason
// TelegramReactiveLoopSteps scopes to "reactiveloop" (see that file for the full rationale): Reqnroll
// owns [Binding] construction, so [BeforeScenario]/[AfterScenario] forward into NeuronTestBase manually.
[Binding]
public sealed class XBitcoinTelegramDemoSteps : NeuronTestBase
{
    private const string PackName = "DigitalBrain.Experience.XBitcoinTelegramDemo";

    private readonly SignalEgressBus _egressBus = new();
    private readonly FakeMarketDataApiClient _fakeClient = new() { Price = "$61,234.56" };
    private SignalEgressBus.Subscription? _egressSubscription;

    [BeforeScenario("xbitcoindemo")]
    public Task BeforeScenarioAsync() => InitializeAsync();

    [AfterScenario("xbitcoindemo")]
    public Task AfterScenarioAsync()
    {
        _egressSubscription?.Dispose();
        return DisposeAsync();
    }

    protected override void ConfigureSilo(ISiloBuilder builder) => builder
        .ConfigureServices(services =>
        {
            services.AddSingleton<IMarketDataApiClient>(_fakeClient);
            services.AddSingleton(_egressBus);
        });

    [Given(@"the X-Bitcoin-Telegram demo pack is installed")]
    public async Task GivenTheDemoPackIsInstalled()
    {
        var market = Grain<IMarketplaceNeuron>("market-xbitcoin-demo");
        await market.FireAsync(new PublishToMarketplace(
            PackName, "1.0.0", Code: MarketplaceSeeds.XBitcoinTelegramDemoPackCode,
            OwnerId: "tester", IsPrivate: false, CommissionRate: 0.0));
        await market.FireAsync(new InstallFromMarketplace(PackName, "1.0.0", BuyerId: "xbitcoin-demo-user"));

        // Force MarketDataNeuron to activate/subscribe before the broadcast chain reaches it in this
        // test (tests don't run the ApplicationStarted lifecycle hook). Production activation is wired
        // in Program.cs's startup warmup, mirroring ILlmResponderNeuron's identical requirement.
        var marketData = Grain<IMarketDataNeuron>("market-data-main");
        await marketData.GetTimelineAsync();
    }

    [Given(@"the egress bus is watching ""(.*)""")]
    public void GivenEgressBusWatching(string signalType) =>
        _egressSubscription = _egressBus.Subscribe(new[] { signalType });

    [When(@"a simulated X post from ""(.*)"" arrives for chat (\d+) with text ""(.*)""")]
    public async Task WhenASimulatedXPostArrives(string author, int chatId, string text)
    {
        var tools = new DigitalBrainMutationTools(new TestGrainFactory(this));
        await tools.SimulateXPost(author, text, chatId);
    }

    // Scoped to "xbitcoindemo": TelegramReactiveLoopSteps binds this exact step text too, and an unscoped
    // duplicate causes Reqnroll.AmbiguousBindingException. A scoped binding is preferred over an unscoped
    // one for a matching tag (docs.reqnroll.net/latest/automation/scoped-bindings.html).
    [Then(@"a ""(.*)"" reply for chat (\d+) with text ""(.*)"" reaches the egress bus")]
    [Scope(Tag = "xbitcoindemo")]
    public async Task ThenAReplyReachesTheEgressBus(string replyType, int chatId, string text)
    {
        Assert.NotNull(_egressSubscription);

        Signal? received = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            while (received is null)
            {
                var signal = await _egressSubscription!.Reader.ReadAsync(cts.Token);
                if (signal.Name == replyType) received = signal;
            }
        }
        catch (OperationCanceledException)
        {
            // received stays null -> the assertion below fails with a clear message.
        }

        Assert.NotNull(received);
        Assert.Equal(chatId, Convert.ToInt32(received!.Props["chatId"]));
        Assert.Equal(text, received.Props["text"]);
    }
}
