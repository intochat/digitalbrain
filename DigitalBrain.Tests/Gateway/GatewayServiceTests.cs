using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Core.Ui;
using DigitalBrain.Demo.Runtime;
using DigitalBrain.Google;
using DigitalBrain.Runtime.Grpc;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Config;
using DigitalBrain.Kernel.Foundry;
using DigitalBrain.Kernel.Gateway;
using DigitalBrain.Kernel.Market;
using DigitalBrain.Salesforce;
using DigitalBrain.Tests.TestSupport;
using DigitalBrain.TestKit;
using DigitalBrain.UiKit;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans.Journaling;
using DigitalBrain.Kernel.Ui;

namespace DigitalBrain.Tests.Gateway;

[Collection("silo-host")]
public class GatewayServiceTests : NeuronTestBase
{
    private readonly HomeFeedBus _homeFeedBus = new();
    private readonly FakeMarketDataApiClient _marketClient = new();

    protected override void ConfigureSilo(ISiloBuilder builder) => builder
        .AddMemoryGrainStorageAsDefault()
        .AddMemoryStreams("Default")
        .AddMemoryStreams("HomeFeed")
        .AddMemoryStreams("DigitalBrainTimeline")
        .AddMemoryGrainStorage("PubSubStore")
        .ConfigureServices(services =>
        {
            services.AddKeyedScoped<IDurableList<Synapse>>("in-journal", (_, _) => new InMemoryDurableList<Synapse>());
            services.AddKeyedScoped<IDurableList<Synapse>>("out-journal", (_, _) => new InMemoryDurableList<Synapse>());
            services.AddScoped<NeuronJournals>();
            services.AddSingleton<IJournaledStateManager, TestJournaledStateManager>();
            services.AddSingleton<IPackEmbodiment, PackAlcEmbodier>();
            services.AddSingleton(_homeFeedBus);
            services.AddSingleton<IMarketDataApiClient>(_marketClient);
        });

    private GatewayService NewService() =>
        new(Cluster.GrainFactory, new ConfigurationBuilder().Build(), _homeFeedBus,
            new SignalEgressBus(),
            new FakeHostEnvironment(),
            NullLogger<GatewayService>.Instance);

    [Fact]
    public async Task ConfigurationProvided_With_Scope_Not_Owned_By_Caller_Is_Rejected()
    {
        // NewService() passes packConfigStore: null, which trips the earlier "store not configured" guard
        // before ever reaching the scope check. Build a service with a real store instead.
        var services = new ServiceCollection();
        services.AddPackConfigStore(blobsForKeyRing: null);
        var packConfigStore = services.BuildServiceProvider().GetRequiredService<IPackConfigStore>();

        var svc = new GatewayService(Cluster.GrainFactory, new ConfigurationBuilder().Build(), _homeFeedBus,
            new SignalEgressBus(), new FakeHostEnvironment(), NullLogger<GatewayService>.Instance, packConfigStore);

        var ex = await Assert.ThrowsAsync<RpcException>(() => svc.Send(new SynapseEnvelope
        {
            TypeName = nameof(ConfigurationProvided),
            Payload = global::Google.Protobuf.ByteString.CopyFrom(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                pack = "some-pack",
                scope = "user:someone-else",
                secretField = "value"
            }))
        }, TestContext()));

        Assert.Equal(StatusCode.PermissionDenied, ex.StatusCode);
    }

    [Fact]
    public async Task InstallFromMarketplace_Ignores_Client_Supplied_BuyerId_When_Unauthenticated()
    {
        using var subscription = _homeFeedBus.Subscribe();
        var svc = NewService();

        await svc.Send(new SynapseEnvelope
        {
            TypeName = nameof(InstallFromMarketplace),
            Payload = global::Google.Protobuf.ByteString.CopyFrom(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                packName = "nonexistent-pack",
                version = "1.0",
                buyerId = "attacker-supplied-victim-id"
            }))
        }, TestContext());

        var market = Grain<IMarketplaceNeuron>("market-main");
        var timeline = await market.GetOutgoingTimelineAsync();
        var install = Assert.Single(timeline.OfType<InstallFromMarketplace>(), i => i.PackName == "nonexistent-pack");
        Assert.Equal("anonymous", install.BuyerId);
    }

    [Fact]
    public async Task Ask_Ino_ReturnsNonEmptyReply()
    {
        var reply = await NewService().Ask(new AskRequest { NeuronId = "ino-main", Prompt = "hello" }, TestContext());
        Assert.False(string.IsNullOrWhiteSpace(reply.Text));
    }

    [Fact]
    public async Task Ask_NonIno_ThrowsInvalidArgument()
    {
        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            NewService().Ask(new AskRequest { NeuronId = "demo-x", Prompt = "hi" }, TestContext()));
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task Fire_ThenTimeline_ShowsDemoMessage()
    {
        var svc = NewService();
        await svc.Fire(new FireRequest { NeuronId = "demo-fire", Text = "ping-123" }, TestContext());

        var timeline = await svc.Timeline(new TimelineRequest { NeuronId = "demo-fire", MaxEntries = 10 }, TestContext());
        Assert.Contains(timeline.Entries, e => e.Type == nameof(DemoMessageSynapse) && e.Text.Contains("ping-123"));
    }

    [Fact]
    public async Task WatchHomeFeed_Writes_Login_Surface_To_New_Client()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var writer = new CapturingServerStreamWriter<RfwCardEnvelope>(() => cts.Cancel());
        var svc = NewService();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            svc.WatchHomeFeed(new WatchHomeFeedRequest(), writer, TestContext(cts.Token)));

        var card = Assert.Single(writer.Messages);
        Assert.Contains("\"kind\":\"login\"", card.DataJson);
        Assert.Contains("\"synapseType\":\"LoginRequest\"", card.DataJson);
    }

    [Fact]
    public async Task WatchHomeFeed_Only_Delivers_Cards_Addressed_To_The_Resolved_Session()
    {
        var svc = NewService();

        await svc.Send(new SynapseEnvelope
        {
            TypeName = nameof(LoginRequest),
            Payload = global::Google.Protobuf.ByteString.CopyFrom(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                username = "feed-isolation-user",
                password = "correct horse battery staple",
                clientId = "test"
            }))
        }, TestContext());

        var session = Grain<IUserSessionNeuron>("session-main");
        var sessionId = (await session.GetOutgoingTimelineAsync()).OfType<UserSessionCreated>().Single().SessionId;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var writer = new CapturingServerStreamWriter<RfwCardEnvelope>();
        var watchTask = svc.WatchHomeFeed(new WatchHomeFeedRequest { SessionId = sessionId }, writer, TestContext(cts.Token));

        for (var attempt = 0; attempt < 40 && writer.Messages.Count == 0; attempt++)
            await Task.Delay(25);

        // The line above only proves the (synchronously-written) initial login card arrived — it says nothing about
        // whether homeFeedBus.Subscribe(sessionId) has run yet: that happens only after WatchHomeFeed awaits the real
        // ResolveSessionAsync grain round-trip, which is genuinely asynchronous and does not complete inline the way
        // the login card's write did. Broadcasting before that subscription is registered would be silently dropped
        // by HomeFeedBus, so give the grain round-trip a bounded, generous window to land before addressing cards.
        for (var attempt = 0; attempt < 40 && writer.Messages.Count < 2; attempt++)
        {
            // DataJson varies per attempt because HomeFeedBus content-hash-dedups identical cards at the point of
            // Broadcast (before any subscriber even sees them) — an unvarying probe would only ever be attempted once.
            _homeFeedBus.Broadcast(new RfwCard("digitalbrain", "ReadinessProbe", $"{{\"attempt\":{attempt}}}", sessionId));
            await Task.Delay(25);
        }

        _homeFeedBus.Broadcast(new RfwCard("digitalbrain", "AddressedToMe", "{}", sessionId));
        _homeFeedBus.Broadcast(new RfwCard("digitalbrain", "AddressedToSomeoneElse", "{}", "someone-elses-session"));
        _homeFeedBus.Broadcast(new RfwCard("digitalbrain", "Unaddressed", "{}"));

        await Task.Delay(300);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => watchTask);

        Assert.Contains(writer.Messages, m => m.RootWidget == "AddressedToMe");
        Assert.Contains(writer.Messages, m => m.RootWidget == "Unaddressed");
        Assert.DoesNotContain(writer.Messages, m => m.RootWidget == "AddressedToSomeoneElse");
    }

    [Fact]
    public async Task WatchHomeFeed_Unauthenticated_Never_Receives_Session_Addressed_Cards()
    {
        var svc = NewService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var writer = new CapturingServerStreamWriter<RfwCardEnvelope>();
        var watchTask = svc.WatchHomeFeed(new WatchHomeFeedRequest(), writer, TestContext(cts.Token));

        for (var attempt = 0; attempt < 40 && writer.Messages.Count == 0; attempt++)
            await Task.Delay(25);

        _homeFeedBus.Broadcast(new RfwCard("digitalbrain", "AddressedToSomeone", "{}", "some-real-session"));
        _homeFeedBus.Broadcast(new RfwCard("digitalbrain", "SystemUnaddressed", "{}"));

        await Task.Delay(300);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => watchTask);

        Assert.Contains(writer.Messages, m => m.RootWidget == "SystemUnaddressed");
        Assert.DoesNotContain(writer.Messages, m => m.RootWidget == "AddressedToSomeone");
    }

    [Fact]
    public async Task Send_InoRequest_Routes_The_Real_Prompt_Not_A_Placeholder()
    {
        var svc = NewService();
        var payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            prompt = "what's the bitcoin price",
            sessionId = "chat-session-1"
        });

        await svc.Send(new SynapseEnvelope
        {
            TypeName = nameof(InoRequest),
            Payload = global::Google.Protobuf.ByteString.CopyFrom(payload)
        }, TestContext());

        var ino = Grain<IInoNeuron>("ino-main");
        var timeline = await ino.GetOutgoingTimelineAsync();
        var response = Assert.Single(timeline.OfType<InoResponse>());
        Assert.Equal("what's the bitcoin price", response.Prompt);
    }

    [Fact]
    public async Task Send_InoRequest_BitcoinPriceIntent_DeliversFormattedPriceSurface()
    {
        _marketClient.Price = "$42,123.45";
        var svc = NewService();

        // InoRequest now resolves sessionId through ResolveSessionAsync (a client-supplied id that never
        // logged in resolves to null, per this task's identity-trust fix), so this test must present a real
        // session — an arbitrary literal like the old "chat-session-btc" would no longer round-trip.
        await svc.Send(new SynapseEnvelope
        {
            TypeName = nameof(LoginRequest),
            Payload = global::Google.Protobuf.ByteString.CopyFrom(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                username = "bitcoin-price-user",
                password = "correct horse battery staple",
                clientId = "test"
            }))
        }, TestContext());

        var session = Grain<IUserSessionNeuron>("session-main");
        var sessionId = (await session.GetOutgoingTimelineAsync()).OfType<UserSessionCreated>().Single().SessionId;

        var payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            prompt = "what's the bitcoin price?",
            sessionId
        });

        await svc.Send(new SynapseEnvelope
        {
            TypeName = nameof(InoRequest),
            Payload = global::Google.Protobuf.ByteString.CopyFrom(payload)
        }, TestContext());

        var flutter = Grain<IFlutterUiNeuron>("flutter-ui");
        var timeline = await flutter.GetIncomingTimelineAsync();

        var surface = Assert.Single(timeline.OfType<UiSurface>());
        Assert.Equal(UiSurface.WidgetTreeKind, surface.Kind);
        Assert.Equal(sessionId, surface.Props["sessionId"]);
        Assert.Equal("assistant", surface.Props["role"]);

        var tree = Assert.IsType<UiWidgetTree>(surface.Props["tree"]);
        Assert.Equal(UiKitVocabulary.Text, tree.Type);
        Assert.Contains("$42,123.45", tree.Props["text"]!.ToString());
    }

    [Fact]
    public async Task Send_GoogleAuthRequested_Routes_To_GoogleAuthNeuron()
    {
        var svc = NewService();

        await svc.Send(new SynapseEnvelope
        {
            TypeName = GoogleSignals.AuthRequested,
            Payload = global::Google.Protobuf.ByteString.CopyFrom(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                sessionId = "chat-session-gmail"
            }))
        }, TestContext());

        var auth = Grain<IGoogleAuthNeuron>("google-auth-main");
        var timeline = await auth.GetOutgoingTimelineAsync();
        var authUrl = Assert.Single(timeline.OfType<Signal>(), signal => signal.Name == GoogleSignals.AuthUrl);
        Assert.Equal("google", authUrl.Props["provider"]);
        Assert.Contains("accounts.google.com", authUrl.Props["url"]!.ToString());
    }

    [Fact]
    public async Task Send_SalesforceAuthRequested_Routes_To_SalesforceAuthNeuron()
    {
        var svc = NewService();

        await svc.Send(new SynapseEnvelope
        {
            TypeName = nameof(LoginRequest),
            Payload = global::Google.Protobuf.ByteString.CopyFrom(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                username = "salesforce-auth-test-user",
                password = "correct horse battery staple",
                clientId = "test"
            }))
        }, TestContext());

        var session = Grain<IUserSessionNeuron>("session-main");
        var sessionId = (await session.GetOutgoingTimelineAsync()).OfType<UserSessionCreated>().Single().SessionId;

        await svc.Send(new SynapseEnvelope
        {
            TypeName = SalesforceSignals.AuthRequested,
            Payload = global::Google.Protobuf.ByteString.CopyFrom(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                sessionId
            }))
        }, TestContext());

        var auth = Grain<ISalesforceAuthNeuron>("salesforce-auth-test-user");
        var timeline = await auth.GetOutgoingTimelineAsync();
        var form = Assert.Single(timeline.OfType<UiSurface>(), surface => surface.Kind == ConfigFormSurface.Kind);
        Assert.Equal("salesforce", form.Props["pack"]);
        Assert.Equal(sessionId, form.Props["sessionId"]);
    }

    [Fact]
    public async Task Send_SalesforceAuthRequested_Routes_To_The_Callers_Own_UserKeyed_Grain()
    {
        var svc = NewService();

        await svc.Send(new SynapseEnvelope
        {
            TypeName = nameof(LoginRequest),
            Payload = global::Google.Protobuf.ByteString.CopyFrom(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                username = "salesforce-test-user",
                password = "correct horse battery staple",
                clientId = "test"
            }))
        }, TestContext());

        var session = Grain<IUserSessionNeuron>("session-main");
        var sessionId = (await session.GetOutgoingTimelineAsync()).OfType<UserSessionCreated>().Single().SessionId;

        await svc.Send(new SynapseEnvelope
        {
            TypeName = SalesforceSignals.AuthRequested,
            Payload = global::Google.Protobuf.ByteString.CopyFrom(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                sessionId
            }))
        }, TestContext());

        var auth = Grain<ISalesforceAuthNeuron>("salesforce-test-user");
        var timeline = await auth.GetOutgoingTimelineAsync();
        var form = Assert.Single(timeline.OfType<UiSurface>(), surface => surface.Kind == ConfigFormSurface.Kind);
        Assert.Equal("salesforce", form.Props["pack"]);
        Assert.Equal(sessionId, form.Props["sessionId"]);
    }

    [Fact]
    public async Task Send_SalesforceAuthRequested_Without_A_Session_Is_Rejected()
    {
        var svc = NewService();

        var ex = await Assert.ThrowsAsync<RpcException>(() => svc.Send(new SynapseEnvelope
        {
            TypeName = SalesforceSignals.AuthRequested,
            Payload = global::Google.Protobuf.ByteString.CopyFrom(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                sessionId = "not-a-real-session"
            }))
        }, TestContext()));

        Assert.Equal(StatusCode.Unauthenticated, ex.StatusCode);
    }

    [Fact]
    public async Task Send_SurfaceDemoRequested_InstallsPack_And_BroadcastsRenderableSurface()
    {
        using var subscription = _homeFeedBus.Subscribe();
        var svc = NewService();

        await svc.Send(new SynapseEnvelope
        {
            TypeName = SurfaceDemoRuntime.RequestType,
            CorrelationId = "ui-demo-test"
        }, TestContext());

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var cards = new List<RfwCard>();
        while (cards.Count < 8 &&
               (!cards.Any(c => c.DataJson.Contains("journaled response and surface update observed", StringComparison.Ordinal)) ||
                !cards.Any(c => c.DataJson.Contains("Embodied pack live", StringComparison.Ordinal))))
        {
            cards.Add(await subscription.Reader.ReadAsync(timeout.Token));
        }

        var graph = Assert.Single(cards, c => c.DataJson.Contains("journaled response and surface update observed", StringComparison.Ordinal));
        Assert.Equal("digitalbrain", graph.LibraryName);
        Assert.Equal("root", graph.RootWidget);
        Assert.Contains("\"kind\":\"activity-graph\"", graph.DataJson);
        Assert.Contains("\"edges\"", graph.DataJson);
        Assert.Contains("\"correlationId\":\"ui-demo-test\"", graph.DataJson);

        var card = Assert.Single(cards, c => c.DataJson.Contains("Embodied pack live", StringComparison.Ordinal));
        Assert.Equal("digitalbrain", card.LibraryName);
        Assert.Equal("root", card.RootWidget);
        Assert.False(string.IsNullOrWhiteSpace(card.CorrelationId));
        Assert.Contains("\"source\"", card.DataJson);
        Assert.Contains("Embodied pack live", card.DataJson);

        var generated = Grain<IGeneratedNeuron>(SurfaceDemoRuntime.GeneratedNeuronKey);
        var timeline = await generated.GetOutgoingTimelineAsync();
        var emittedSurface = Assert.Single(timeline.OfType<UiSurface>(), surface =>
            surface.Props.TryGetValue(UiSurfaceKeys.SurfaceId, out var id) &&
            Equals(id, "surface-demo-pack"));
        Assert.Equal("ui-demo-test", emittedSurface.CorrelationId);
        Assert.False(string.IsNullOrWhiteSpace(emittedSurface.CausationId));

        var observability = Grain<IObservabilityNeuron>(SurfaceDemoRuntime.ObservabilityNeuronKey);
        var graphTimeline = await observability.GetOutgoingTimelineAsync();
        Assert.Contains(graphTimeline.OfType<UiSurface>(), surface =>
            surface.Kind == UiSurfaceKinds.ActivityGraph &&
            surface.CorrelationId == "ui-demo-test");
    }

    private static ServerCallContext TestContext(CancellationToken cancellationToken = default) =>
        TestServerCallContext.Create(cancellationToken);

    private sealed class CapturingServerStreamWriter<T>(Action? afterFirstWrite = null) : IServerStreamWriter<T>
    {
        public List<T> Messages { get; } = new();
        public WriteOptions? WriteOptions { get; set; }

        public Task WriteAsync(T message)
        {
            Messages.Add(message);
            if (Messages.Count == 1)
            {
                afterFirstWrite?.Invoke();
            }

            return Task.CompletedTask;
        }
    }
}
