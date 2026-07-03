using System.Threading.Channels;
using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Runtime.Grpc;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Config;
using DigitalBrain.Kernel.Foundry;
using DigitalBrain.Kernel.Gateway;
using DigitalBrain.Kernel.Ui;
using DigitalBrain.Tests.Kernel;
using DigitalBrain.Tests.TestSupport;
using DigitalBrain.TestKit;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans.Journaling;
using Orleans.TestingHost;
using Reqnroll;

namespace DigitalBrain.Tests.Steps;

// End-to-end proof of the full Telegram reactive loop over a real TestCluster, LLM stubbed:
// Send("TelegramMessageReceived") -> embodied responder pack -> AskLlm (broadcast) ->
// LlmResponderNeuron (fake IChatClient -> "ANSWER:hi") -> Signal("TelegramReplyRequested") -> egress bus.
//
// Reqnroll owns construction of [Binding] classes via its own per-scenario DI container, so this class
// can't rely on xUnit to drive NeuronTestBase's IAsyncLifetime the way NeuronTestBase's own [Fact]-based
// consumers do (see PackSpecSteps.cs for the full rationale). [BeforeScenario]/[AfterScenario] forward
// into NeuronTestBase.InitializeAsync()/DisposeAsync() instead. Scoped to "reactiveloop", not "e2e" —
// "e2e" is also on the sibling N+1 scenario below, and a shared tag would spin up this binding class's
// TestCluster for that scenario too even though none of its steps match.
[Binding]
public sealed class TelegramReactiveLoopSteps : NeuronTestBase
{
    // Shared so the in-cluster grains and the out-of-cluster GatewayService read/write the same backing store.
    private IPackConfigStore _configStore = null!;

    private readonly SignalEgressBus _egressBus = new();
    private const string PackName = "TelegramResponderNeuron";
    private const string Scope = "telegram-loop-user";

    private SignalEgressBus.Subscription? _egressSubscription;

    [BeforeScenario("reactiveloop")]
    public Task BeforeScenarioAsync()
    {
        var configServices = new ServiceCollection();
        configServices.AddDataProtection().UseEphemeralDataProtectionProvider();
        configServices.AddSingleton<IPackConfigBackingStore>(new InMemoryPackConfigBackingStore());
        configServices.AddSingleton<IPackConfigStore, PackConfigStore>();
        _configStore = configServices.BuildServiceProvider().GetRequiredService<IPackConfigStore>();

        return InitializeAsync();
    }

    [AfterScenario("reactiveloop")]
    public Task AfterScenarioAsync()
    {
        _egressSubscription?.Dispose();
        return DisposeAsync();
    }

    // NeuronTestSiloConfigurator (DigitalBrain.TestKit/NeuronTestSiloConfigurator.cs) already wires the
    // shared journal/embodiment/streams plumbing that the deleted TelegramReactiveLoopSiloConfig
    // hand-rolled. Only the Telegram-specific extras go here: a deterministic global IChatClient
    // (NeuronTestSiloConfigurator's IScopedChatClientFactory is a no-op, so LlmResponderNeuron falls
    // back to this global client), and this scenario's own SignalEgressBus/IPackConfigStore instances —
    // registered after (so they win last-registration-wins resolution over) NeuronTestSiloConfigurator's
    // own SignalEgressBus, so the silo's SignalEgressStreamSubscriber and this class's _egressSubscription
    // observe the same bus.
    protected override void ConfigureSilo(ISiloBuilder builder) => builder
        .ConfigureServices(services =>
        {
            services.AddSingleton<IChatClient, AnswerPrefixChatClient>();
            services.AddSingleton(_egressBus);
            services.AddSingleton(_configStore);
        });

    [Given(@"the Telegram responder experience is installed")]
    public async Task GivenTheTelegramResponderExperienceIsInstalled()
    {
        var market = Grain<IMarketplaceNeuron>("market-telegram-loop");
        await market.FireAsync(new PublishToMarketplace(
            PackName, "1.0", Code: MarketplaceSeeds.TelegramResponderPackCode,
            OwnerId: "tester", IsPrivate: false, CommissionRate: 0.0));
        await market.FireAsync(new InstallFromMarketplace(PackName, "1.0", BuyerId: Scope));
    }

    [Then(@"the install emits a config form whose tree contains the fields ""(.*)"", ""(.*)"", ""(.*)""")]
    public async Task ThenTheInstallEmitsAConfigForm(string field1, string field2, string field3)
    {
        var gen = Grain<IGeneratedNeuron>("generated-" + PackName.ToLowerInvariant());

        UiSurface? form = null;
        for (var attempt = 0; attempt < 40 && form is null; attempt++)
        {
            var timeline = await gen.GetTimelineAsync();
            form = timeline.OfType<UiSurface>().FirstOrDefault(s => s.Kind == ConfigFormSurface.Kind);
            if (form is null) await Task.Delay(50);
        }

        Assert.NotNull(form);
        var tree = Assert.IsType<UiWidgetTree>(form!.Props["tree"]);
        var keys = CollectFieldKeys(tree);
        Assert.Contains(field1, keys);
        Assert.Contains(field2, keys);
        Assert.Contains(field3, keys);
    }

    [When(@"I provide the Telegram configuration token ""(.*)"", provider ""(.*)"", key ""(.*)""")]
    public async Task WhenIProvideTheTelegramConfiguration(string token, string provider, string key)
    {
        var values = new Dictionary<string, string>
        {
            ["telegram_token"] = token,
            ["llm_provider"] = provider,
            ["llm_key"] = key,
            ["pack"] = PackName,
            ["scope"] = Scope
        };
        var payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(values);

        var gateway = new GatewayService(
            Cluster.GrainFactory,
            new ConfigurationBuilder().Build(),
            new HomeFeedBus(),
            new SignalEgressBus(),
            new FakeHostEnvironment(),
            NullLogger<GatewayService>.Instance,
            _configStore);

        await gateway.Send(new SynapseEnvelope
        {
            TypeName = nameof(ConfigurationProvided),
            Payload = global::Google.Protobuf.ByteString.CopyFrom(payload)
        }, TestServerCallContext.Create());

        var stored = await _configStore.GetAsync(Scope, PackName);
        Assert.Equal(token, stored["telegram_token"]);
    }

    [When(@"the LLM responder is active and the egress bus is watching ""(.*)""")]
    public async Task WhenTheResponderIsActiveAndEgressIsWatching(string replyType)
    {
        // Activate the responder so it subscribes to the timeline before the AskLlm broadcast arrives.
        // Production will need a startup activation of LlmResponderNeuron (slice-5 / Program.cs concern).
        var responder = Grain<ILlmResponderNeuron>("telegram-loop-responder");
        await responder.GetTimelineAsync();

        _egressSubscription = _egressBus.Subscribe(new[] { replyType });
    }

    [When(@"a Telegram message arrives for chat (\d+) with text ""(.*)""")]
    public async Task WhenATelegramMessageArrives(int chatId, string text)
    {
        // Mirrors the generic Send -> IngressNeuron path: broadcast a named Signal on the timeline.
        var ingress = Grain<IIngressNeuron>("telegram-loop-ingress");
        await ingress.IngestAsync("TelegramMessageReceived",
            new Dictionary<string, object?> { ["chatId"] = chatId, ["text"] = text });
    }

    [Then(@"the embodied pack emits an AskLlm for ""(.*)""")]
    public async Task ThenTheEmbodiedPackEmitsAnAskLlm(string prompt)
    {
        var gen = Grain<IGeneratedNeuron>("generated-" + PackName.ToLowerInvariant());

        AskLlm? ask = null;
        for (var attempt = 0; attempt < 40 && ask is null; attempt++)
        {
            var timeline = await gen.GetTimelineAsync();
            ask = timeline.OfType<AskLlm>().FirstOrDefault(a => a.Prompt == prompt);
            if (ask is null) await Task.Delay(50);
        }

        Assert.NotNull(ask);
        Assert.Equal("TelegramReplyRequested", ask!.ReplyType);
    }

    [Then(@"a ""(.*)"" reply for chat (\d+) with text ""(.*)"" reaches the egress bus")]
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
        Assert.Equal(replyType, received!.Name);
        Assert.True(received.Props.TryGetValue("chatId", out var rawChatId), "Reply must carry chatId");
        Assert.True(Convert.ToInt32(rawChatId) == chatId, $"chatId should be {chatId}, was {rawChatId}");
        Assert.Equal(text, received.Props["text"]);
    }

    private static IReadOnlyList<string> CollectFieldKeys(UiWidgetTree tree) =>
        FindNodes(tree)
            .Where(n => n.Type == DigitalBrain.Core.UiKitVocabulary.TextField || n.Type == DigitalBrain.Core.UiKitVocabulary.Select)
            .Select(n => n.Props.GetValueOrDefault("key")?.ToString() ?? n.Props.GetValueOrDefault("name")?.ToString())
            .OfType<string>()
            .ToList();

    private static IEnumerable<UiWidgetTree> FindNodes(UiWidgetTree node)
    {
        yield return node;
        if (node.Children is null) yield break;
        foreach (var child in node.Children)
            foreach (var descendant in FindNodes(child))
                yield return descendant;
    }
}

// N+1 reactivity proof: two packs (TelegramResponderNeuron + KeywordWatcherNeuron) both react to ONE
// TelegramMessageReceived broadcast — proving N+1 handler count with no silo restart. Scoped to "n1", not
// "e2e", for the same reason TelegramReactiveLoopSteps above is scoped to "reactiveloop": "e2e" is also on
// the sibling "Full reactive loop" scenario, and a shared tag would spin up this binding class's TestCluster
// for that scenario too even though none of its steps match.
[Binding]
public sealed class TelegramN1ReactivitySteps : NeuronTestBase
{
    // Shared so the in-cluster grains and the out-of-cluster GatewayService read/write the same backing store.
    private IPackConfigStore _configStore = null!;

    private readonly SignalEgressBus _egressBus = new();
    private const string ResponderPackName = "TelegramResponderNeuron";
    private const string WatcherPackName   = "KeywordWatcherNeuron";
    private const string N1Scope           = "n1-reactivity-user";

    // Signals collected from the egress bus in arrival order; both Then-steps read from this list.
    private readonly List<Signal> _collectedSignals = new();
    private SignalEgressBus.Subscription? _egressSubscription;

    [BeforeScenario("n1")]
    public Task BeforeScenarioAsync()
    {
        var configServices = new ServiceCollection();
        configServices.AddDataProtection().UseEphemeralDataProtectionProvider();
        configServices.AddSingleton<IPackConfigBackingStore>(new InMemoryPackConfigBackingStore());
        configServices.AddSingleton<IPackConfigStore, PackConfigStore>();
        _configStore = configServices.BuildServiceProvider().GetRequiredService<IPackConfigStore>();

        return InitializeAsync();
    }

    [AfterScenario("n1")]
    public Task AfterScenarioAsync()
    {
        _egressSubscription?.Dispose();
        return DisposeAsync();
    }

    // NeuronTestSiloConfigurator (DigitalBrain.TestKit/NeuronTestSiloConfigurator.cs) already wires the
    // shared journal/embodiment/streams plumbing that the deleted TelegramN1SiloConfig hand-rolled. Only the
    // Telegram-specific extras go here: a deterministic global IChatClient (NeuronTestSiloConfigurator's
    // IScopedChatClientFactory is a no-op, so LlmResponderNeuron falls back to this global client), and this
    // scenario's own SignalEgressBus/IPackConfigStore instances — registered after (so they win last-
    // registration-wins resolution over) NeuronTestSiloConfigurator's own SignalEgressBus, so the silo's
    // SignalEgressStreamSubscriber and this class's _egressSubscription observe the same bus.
    protected override void ConfigureSilo(ISiloBuilder builder) => builder
        .ConfigureServices(services =>
        {
            services.AddSingleton<IChatClient, AnswerPrefixChatClient>();
            services.AddSingleton(_egressBus);
            services.AddSingleton(_configStore);
        });

    [Given(@"I provide the Telegram configuration token ""(.*)"", provider ""(.*)"", key ""(.*)""")]
    public async Task GivenN1TelegramConfig(string token, string provider, string key)
    {
        var values = new Dictionary<string, string>
        {
            ["telegram_token"] = token,
            ["llm_provider"]   = provider,
            ["llm_key"]        = key,
            ["pack"]           = ResponderPackName,
            ["scope"]          = N1Scope
        };
        var payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(values);

        var gateway = new GatewayService(
            Cluster.GrainFactory,
            new ConfigurationBuilder().Build(),
            new HomeFeedBus(),
            new SignalEgressBus(),
            new FakeHostEnvironment(),
            NullLogger<GatewayService>.Instance,
            _configStore);

        await gateway.Send(new SynapseEnvelope
        {
            TypeName = nameof(ConfigurationProvided),
            Payload  = global::Google.Protobuf.ByteString.CopyFrom(payload)
        }, TestServerCallContext.Create());
    }

    [Given(@"both the Telegram responder and the keyword watcher are installed")]
    public async Task GivenBothPacksAreInstalled()
    {
        var market = Grain<IMarketplaceNeuron>("market-n1-proof");

        await market.FireAsync(new PublishToMarketplace(
            ResponderPackName, "1.0", Code: MarketplaceSeeds.TelegramResponderPackCode,
            OwnerId: "tester", IsPrivate: false, CommissionRate: 0.0));
        await market.FireAsync(new InstallFromMarketplace(ResponderPackName, "1.0", BuyerId: N1Scope));

        await market.FireAsync(new PublishToMarketplace(
            WatcherPackName, "1.0", Code: MarketplaceSeeds.KeywordWatcherPackCode,
            OwnerId: "tester", IsPrivate: false, CommissionRate: 0.0));
        await market.FireAsync(new InstallFromMarketplace(WatcherPackName, "1.0", BuyerId: N1Scope));
    }

    [Given(@"the LLM responder is active and the egress bus is watching ""(.*)"" and ""(.*)""")]
    public async Task GivenResponderActiveAndEgressWatchingTwo(string type1, string type2)
    {
        var responder = Grain<ILlmResponderNeuron>("n1-llm-responder");
        await responder.GetTimelineAsync();

        _egressSubscription = _egressBus.Subscribe(new[] { type1, type2 });

        // Drain the channel into _collectedSignals so both Then-steps can assert without consuming each other's signals.
        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var signal in _egressSubscription.Reader.ReadAllAsync())
                    lock (_collectedSignals) _collectedSignals.Add(signal);
            }
            catch (OperationCanceledException) { }
            catch (ChannelClosedException) { }
        });
    }

    [When(@"a Telegram message with text ""(.*)"" is ingested for chat (\d+)")]
    public async Task WhenATelegramMessageIsIngestedForChat(string text, int chatId)
    {
        var ingress = Grain<IIngressNeuron>("n1-ingress");
        await ingress.IngestAsync("TelegramMessageReceived",
            new Dictionary<string, object?> { ["chatId"] = chatId, ["text"] = text });
    }

    [Then(@"a ""(.*)"" reply for chat (\d+) reaches the egress bus")]
    public async Task ThenAReplyForChatReachesEgressBus(string signalName, int chatId)
    {
        var received = await WaitForSignalAsync(signalName, chatId);

        Assert.NotNull(received);
        Assert.Equal(signalName, received!.Name);
        Assert.Equal(chatId, Convert.ToInt32(received.Props.GetValueOrDefault("chatId")));

        if (signalName == "TelegramReplyRequested")
        {
            var text = received.Props.GetValueOrDefault("text")?.ToString() ?? "";
            Assert.StartsWith("ANSWER:", text);
        }
    }

    [Then(@"a ""(.*)"" signal for chat (\d+) reaches the egress bus")]
    public async Task ThenAReminderSignalForChatReachesEgressBus(string signalName, int chatId)
    {
        var received = await WaitForSignalAsync(signalName, chatId);

        Assert.NotNull(received);
        Assert.Equal(signalName, received!.Name);
        Assert.Equal(chatId, Convert.ToInt32(received.Props.GetValueOrDefault("chatId")));
        var reminder = received.Props.GetValueOrDefault("reminder")?.ToString() ?? "";
        Assert.Contains("remind me", reminder, StringComparison.OrdinalIgnoreCase);
    }

    // Polls _collectedSignals (fed by a background pump) so both assertions read from the same captured list
    // without consuming each other's signals.
    private async Task<Signal?> WaitForSignalAsync(string name, int chatId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            lock (_collectedSignals)
            {
                var match = _collectedSignals.FirstOrDefault(s =>
                    s.Name == name && Convert.ToInt32(s.Props.GetValueOrDefault("chatId")) == chatId);
                if (match is not null) return match;
            }
            await Task.Delay(50);
        }
        return null;
    }
}
