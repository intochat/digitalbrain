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
using DigitalBrain.Kernel.Voice;
using DigitalBrain.Salesforce;
using DigitalBrain.Tests.TestSupport;
using DigitalBrain.TestKit;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans.Journaling;
using Orleans.TestingHost;
using DigitalBrain.Kernel.Ui;

namespace DigitalBrain.Tests.Gateway;

[Collection("kernel-host")]
public class GatewayServiceTests : NeuronTestBase
{
    private HomeFeedBus? _homeFeedBusInstance;
    private readonly FakeMarketDataApiClient _marketClient = new();

    // Lazily resolved via the silo's own DI container (same pattern as HomeFeedCrossSiloTests) — HomeFeedBus
    // now requires a real IClusterClient, which is only available once the cluster has finished starting,
    // i.e. after NeuronTestBase.InitializeAsync runs. A field initializer would run too early.
    private HomeFeedBus HomeFeedBus => _homeFeedBusInstance ??=
        ((InProcessSiloHandle)Cluster.Silos[0]).SiloHost.Services.GetRequiredService<HomeFeedBus>();

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
            services.AddSingleton<HomeFeedBus>();
            services.AddSingleton<IMarketDataApiClient>(_marketClient);
        });

    private GatewayService NewService() =>
        new(Cluster.GrainFactory, new ConfigurationBuilder().Build(), HomeFeedBus,
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

        var svc = new GatewayService(Cluster.GrainFactory, new ConfigurationBuilder().Build(), HomeFeedBus,
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
        await using var subscription = await HomeFeedBus.SubscribeAsync(clientId: null);
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
    public async Task InstallFromMarketplace_Resolves_Real_Buyer_From_ClientId_After_Login()
    {
        var svc = NewService();

        await svc.Send(new SynapseEnvelope
        {
            TypeName = nameof(LoginRequest),
            Payload = global::Google.Protobuf.ByteString.CopyFrom(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                username = "install-clientid-user",
                password = "correct horse battery staple",
                clientId = "install-connection"
            }))
        }, TestContext());

        await svc.Send(new SynapseEnvelope
        {
            TypeName = nameof(InstallFromMarketplace),
            Payload = global::Google.Protobuf.ByteString.CopyFrom(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                packName = "nonexistent-pack-2",
                version = "1.0",
                clientId = "install-connection"
            }))
        }, TestContext());

        var market = Grain<IMarketplaceNeuron>("market-main");
        var timeline = await market.GetOutgoingTimelineAsync();
        var install = Assert.Single(timeline.OfType<InstallFromMarketplace>(), i => i.PackName == "nonexistent-pack-2");
        Assert.Equal("install-clientid-user", install.BuyerId);
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
    public async Task Transcribe_StreamsAudioChunks_ToVoiceTranscriber()
    {
        var transcriber = new RecordingVoiceTranscriber();
        var svc = new GatewayService(
            Cluster.GrainFactory,
            new ConfigurationBuilder().Build(),
            HomeFeedBus,
            new SignalEgressBus(),
            new FakeHostEnvironment(),
            NullLogger<GatewayService>.Instance,
            voiceTranscriber: transcriber);

        var reply = await svc.Transcribe(new ListAsyncStreamReader<TranscribeRequest>(new[]
        {
            new TranscribeRequest
            {
                MimeType = "audio/wav",
                AudioChunk = global::Google.Protobuf.ByteString.CopyFrom(new byte[] { 1, 2 })
            },
            new TranscribeRequest
            {
                LanguageHint = "en",
                AudioChunk = global::Google.Protobuf.ByteString.CopyFrom(new byte[] { 3 })
            }
        }), TestContext());

        Assert.Equal("turn on the lights", reply.Transcript);
        Assert.Equal("en", reply.DetectedLanguage);
        Assert.False(string.IsNullOrWhiteSpace(reply.CorrelationId));

        var captured = Assert.Single(transcriber.Requests);
        Assert.Equal(new byte[] { 1, 2, 3 }, captured.Audio);
        Assert.Equal("audio/wav", captured.MimeType);
        Assert.Equal("en", captured.LanguageHint);
        Assert.Equal(reply.CorrelationId, captured.CorrelationId);
    }

    [Fact]
    public async Task Transcribe_WithoutAudio_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            NewService().Transcribe(new ListAsyncStreamReader<TranscribeRequest>(new[]
            {
                new TranscribeRequest { MimeType = "audio/wav" }
            }), TestContext()));

        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
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
    public async Task WatchHomeFeed_Only_Delivers_Cards_Addressed_To_This_Connections_ClientId()
    {
        var svc = NewService();
        const string myClientId = "feed-isolation-client";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var writer = new CapturingServerStreamWriter<RfwCardEnvelope>();
        var watchTask = svc.WatchHomeFeed(new WatchHomeFeedRequest { ClientId = myClientId }, writer, TestContext(cts.Token));

        await AsyncTestWait.WaitUntilAsync(
            () => writer.Messages.Count > 0,
            "initial home-feed login card");

        // The line above only proves the synchronously written initial login card arrived. Subscription setup is
        // asynchronous, so probe with unique cards until the personal stream receives one.
        var readinessAttempt = 0;
        await AsyncTestWait.WaitUntilAsync(async () =>
        {
            var attempt = Interlocked.Increment(ref readinessAttempt);
            await HomeFeedBus.BroadcastAsync(new RfwCard("digitalbrain", "ReadinessProbe", $"{{\"attempt\":{attempt}}}", myClientId));
            return writer.Messages.Count >= 2;
        }, "home-feed personal subscription readiness");
        await HomeFeedBus.BroadcastAsync(new RfwCard("digitalbrain", "AddressedToMe", "{}", myClientId));
        await HomeFeedBus.BroadcastAsync(new RfwCard("digitalbrain", "AddressedToSomeoneElse", "{}", "someone-elses-client-id"));
        await HomeFeedBus.BroadcastAsync(new RfwCard("digitalbrain", "Unaddressed", "{}"));

        await AsyncTestWait.WaitUntilAsync(
            () => writer.Messages.Any(m => m.RootWidget == "AddressedToMe") &&
                  writer.Messages.Any(m => m.RootWidget == "Unaddressed"),
            "addressed and unaddressed home-feed cards");
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => watchTask);

        Assert.Contains(writer.Messages, m => m.RootWidget == "AddressedToMe");
        Assert.Contains(writer.Messages, m => m.RootWidget == "Unaddressed");
        Assert.DoesNotContain(writer.Messages, m => m.RootWidget == "AddressedToSomeoneElse");
    }

    [Fact]
    public async Task WatchHomeFeed_Without_A_ClientId_Never_Receives_Cards_Addressed_To_Someone_Else()
    {
        var svc = NewService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var writer = new CapturingServerStreamWriter<RfwCardEnvelope>();
        var watchTask = svc.WatchHomeFeed(new WatchHomeFeedRequest(), writer, TestContext(cts.Token));

        await AsyncTestWait.WaitUntilAsync(
            () => writer.Messages.Count > 0,
            "initial home-feed login card");

        await HomeFeedBus.BroadcastAsync(new RfwCard("digitalbrain", "AddressedToSomeone", "{}", "some-real-client-id"));
        await HomeFeedBus.BroadcastAsync(new RfwCard("digitalbrain", "SystemUnaddressed", "{}"));

        await AsyncTestWait.WaitUntilAsync(
            () => writer.Messages.Any(m => m.RootWidget == "SystemUnaddressed"),
            "unaddressed home-feed card");
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
            clientId = "chat-client-1"
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
        const string myClientId = "bitcoin-price-connection";

        await svc.Send(new SynapseEnvelope
        {
            TypeName = nameof(LoginRequest),
            Payload = global::Google.Protobuf.ByteString.CopyFrom(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                username = "bitcoin-price-user",
                password = "correct horse battery staple",
                clientId = myClientId
            }))
        }, TestContext());

        var payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            prompt = "what's the bitcoin price?",
            clientId = myClientId
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
        Assert.Equal(myClientId, surface.Props["clientId"]);
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
        const string myClientId = "salesforce-auth-connection";

        await svc.Send(new SynapseEnvelope
        {
            TypeName = nameof(LoginRequest),
            Payload = global::Google.Protobuf.ByteString.CopyFrom(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                username = "salesforce-auth-test-user",
                password = "correct horse battery staple",
                clientId = myClientId
            }))
        }, TestContext());

        await svc.Send(new SynapseEnvelope
        {
            TypeName = SalesforceSignals.AuthRequested,
            Payload = global::Google.Protobuf.ByteString.CopyFrom(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                clientId = myClientId
            }))
        }, TestContext());

        var auth = Grain<ISalesforceAuthNeuron>("salesforce-auth-test-user");
        var timeline = await auth.GetOutgoingTimelineAsync();
        var form = Assert.Single(timeline.OfType<UiSurface>(), surface => surface.Kind == ConfigFormSurface.Kind);
        Assert.Equal("salesforce", form.Props["pack"]);
        Assert.Equal(myClientId, form.Props["clientId"]);
    }

    [Fact]
    public async Task Send_SalesforceAuthRequested_Routes_To_The_Callers_Own_UserKeyed_Grain()
    {
        var svc = NewService();
        const string myClientId = "salesforce-connection";

        await svc.Send(new SynapseEnvelope
        {
            TypeName = nameof(LoginRequest),
            Payload = global::Google.Protobuf.ByteString.CopyFrom(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                username = "salesforce-test-user",
                password = "correct horse battery staple",
                clientId = myClientId
            }))
        }, TestContext());

        await svc.Send(new SynapseEnvelope
        {
            TypeName = SalesforceSignals.AuthRequested,
            Payload = global::Google.Protobuf.ByteString.CopyFrom(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                clientId = myClientId
            }))
        }, TestContext());

        var auth = Grain<ISalesforceAuthNeuron>("salesforce-test-user");
        var timeline = await auth.GetOutgoingTimelineAsync();
        var form = Assert.Single(timeline.OfType<UiSurface>(), surface => surface.Kind == ConfigFormSurface.Kind);
        Assert.Equal("salesforce", form.Props["pack"]);
        Assert.Equal(myClientId, form.Props["clientId"]);
    }

    [Fact]
    public async Task Send_SalesforceAuthRequested_Without_A_Real_Session_Is_Rejected()
    {
        var svc = NewService();

        var ex = await Assert.ThrowsAsync<RpcException>(() => svc.Send(new SynapseEnvelope
        {
            TypeName = SalesforceSignals.AuthRequested,
            Payload = global::Google.Protobuf.ByteString.CopyFrom(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                clientId = "never-logged-in-connection"
            }))
        }, TestContext()));

        Assert.Equal(StatusCode.Unauthenticated, ex.StatusCode);
    }

    [Fact]
    public async Task Send_SurfaceDemoRequested_InstallsPack_And_BroadcastsRenderableSurface()
    {
        await using var subscription = await HomeFeedBus.SubscribeAsync(clientId: null);
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
                !cards.Any(IsSurfaceDemoPackCard)))
        {
            cards.Add(await subscription.Reader.ReadAsync(timeout.Token));
        }

        var graph = cards.FirstOrDefault(c => c.DataJson.Contains("journaled response and surface update observed", StringComparison.Ordinal));
        Assert.NotNull(graph);
        Assert.Equal("digitalbrain", graph!.LibraryName);
        Assert.Equal("root", graph.RootWidget);
        Assert.Contains("\"kind\":\"activity-graph\"", graph.DataJson);
        Assert.Contains("\"edges\"", graph.DataJson);
        Assert.Contains("\"correlationId\":\"ui-demo-test\"", graph.DataJson);

        var card = cards.FirstOrDefault(c => IsSurfaceDemoPackCard(c) && c.DataJson.Contains("Embodied pack live", StringComparison.Ordinal));
        Assert.NotNull(card);
        Assert.Equal("digitalbrain", card!.LibraryName);
        Assert.Equal("root", card.RootWidget);
        Assert.False(string.IsNullOrWhiteSpace(card.CorrelationId));
        Assert.Contains("\"source\"", card.DataJson);

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

        static bool IsSurfaceDemoPackCard(RfwCard card) =>
            card.DataJson.Contains("\"surfaceId\":\"surface-demo-pack\"", StringComparison.Ordinal) &&
            card.DataJson.Contains("\"kind\":\"task-window\"", StringComparison.Ordinal);
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

    private sealed class ListAsyncStreamReader<T>(IEnumerable<T> messages) : IAsyncStreamReader<T>
    {
        private readonly IEnumerator<T> enumerator = messages.GetEnumerator();

        public T Current { get; private set; } = default!;

        public Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<bool>(cancellationToken);
            }

            if (!enumerator.MoveNext())
            {
                return Task.FromResult(false);
            }

            Current = enumerator.Current;
            return Task.FromResult(true);
        }
    }

    private sealed class RecordingVoiceTranscriber : IVoiceTranscriber
    {
        public List<VoiceTranscriptionRequest> Requests { get; } = [];

        public Task<VoiceTranscriptionResult> TranscribeAsync(
            VoiceTranscriptionRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new VoiceTranscriptionResult(
                "turn on the lights",
                "en",
                request.CorrelationId));
        }
    }
}

// Isolated from GatewayServiceTests's own silo config: this is the only test in the file that needs a real
// IPackConfigStore + a working (non-throwing) ISalesforceApiClientFactory, so SalesforceCrmNeuron can actually
// activate and answer a query. Kept separate rather than added to GatewayServiceTests.ConfigureSilo so the
// other Salesforce tests there (which exercise the direct SalesforceSignals.AuthRequested button-click branch,
// not this chat-driven InoRequest branch) are unaffected.
//
// NOTE ON THIS TEST'S DESIGN: the brief for this task specified this test's body checking
// Grain<ISalesforceAuthNeuron>("salesforce-via-chat-user").GetOutgoingTimelineAsync() for a ConfigFormSurface.
// That assertion cannot pass against this codebase's actual architecture: InoNeuron's chat-driven Salesforce
// intent handling (HandleSalesforceIntentAsync / DeliverSalesforceCredentialSurfaceAsync) never touches
// ISalesforceAuthNeuron at all — it builds and delivers its own credential-form surface directly to the
// "flutter-ui" grain (confirmed by tracing InoNeuron.cs and by an instrumented run showing
// ISalesforceAuthNeuron's outgoing timeline is empty of UiSurfaces while flutter-ui's incoming timeline
// does receive the pack-config-form surface). Only the direct SalesforceSignals.AuthRequested branch
// (GatewayService.cs, handled by SalesforceAuthNeuron.cs — Task 6's territory, not touched here) routes to a
// per-user ISalesforceAuthNeuron grain. Additionally, GatewayServiceTests.NewService() never configures an
// IPackConfigStore, so HasSalesforceCredentialAsync short-circuits to false for every user regardless of
// clientId resolution — meaning even a grain-routing fix alone would not make the original assertion an
// actual proof of "resolves real user, not anonymous".
//
// This rewritten version proves the same claim in a way that is actually falsifiable: it stores a Salesforce
// credential scoped ONLY to "salesforce-via-chat-user" (DigitalBrain.Core.PackConfigScopes.ForUser), not the
// shared "default"/app scope, then confirms the chat-driven query actually reaches Salesforce under that exact
// user scope. If ResolveUserIdAsync still resolved the clientId to "anonymous" (the pre-fix bug), the
// user-scoped credential would be invisible and the flow would ask for credentials instead of querying.
public sealed class GatewayServiceSalesforceViaChatIdentityTests : NeuronTestBase
{
    private readonly FakeMarketDataApiClient _marketClient = new();
    private readonly RecordingSalesforceApiClientFactory _salesforceFactory = new();
    private HomeFeedBus? _homeFeedBusInstance;

    private HomeFeedBus HomeFeedBus => _homeFeedBusInstance ??=
        ((InProcessSiloHandle)Cluster.Silos[0]).SiloHost.Services.GetRequiredService<HomeFeedBus>();

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
            services.AddSingleton<HomeFeedBus>();
            services.AddSingleton<IMarketDataApiClient>(_marketClient);
            services.AddPackConfigStore(blobsForKeyRing: null);
            services.AddSingleton<ISalesforceApiClientFactory>(_salesforceFactory);
        });

    private GatewayService NewService() =>
        new(Cluster.GrainFactory, new ConfigurationBuilder().Build(), HomeFeedBus,
            new SignalEgressBus(),
            new FakeHostEnvironment(),
            NullLogger<GatewayService>.Instance);

    [Fact]
    public async Task Send_SalesforceAuthRequested_Via_InoRequest_Resolves_Real_User_Not_Anonymous()
    {
        const string realUserId = "salesforce-via-chat-user";
        const string clientId = "chat-connection-1";
        var svc = NewService();

        await svc.Send(new SynapseEnvelope
        {
            TypeName = nameof(LoginRequest),
            Payload = global::Google.Protobuf.ByteString.CopyFrom(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                username = realUserId,
                password = "correct horse battery staple",
                clientId
            }))
        }, TestContext());

        var store = ((InProcessSiloHandle)Cluster.Silos[0]).SiloHost.Services.GetRequiredService<IPackConfigStore>();
        await store.SetAsync(PackConfigScopes.ForUser(new UserId(realUserId)), SalesforceClientFactory.PackName, new Dictionary<string, string>
        {
            [SalesforceClientFactory.ClientIdKey] = "connected-app-client-id",
            [SalesforceClientFactory.ClientSecretKey] = "connected-app-secret",
            [SalesforceClientFactory.UsernameKey] = "salesforce-user@example.com",
            [SalesforceClientFactory.PasswordKey] = "salesforce-password"
        });

        await svc.Send(new SynapseEnvelope
        {
            TypeName = nameof(InoRequest),
            Payload = global::Google.Protobuf.ByteString.CopyFrom(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                prompt = "check my salesforce accounts",
                clientId
            }))
        }, TestContext());

        var scope = Assert.Single(_salesforceFactory.Scopes);
        Assert.Equal(new UserId(realUserId), scope.UserId);

        var ino = Grain<IInoNeuron>("ino-main");
        var response = Assert.Single((await ino.GetOutgoingTimelineAsync()).OfType<InoResponse>());
        Assert.Contains("Acme Test Corp", response.Response);
    }

    private static ServerCallContext TestContext(CancellationToken cancellationToken = default) =>
        TestServerCallContext.Create(cancellationToken);
}

internal sealed class RecordingSalesforceApiClientFactory : ISalesforceApiClientFactory
{
    public List<NeuronScope> Scopes { get; } = [];

    public Task<ISalesforceApiClient> CreateAsync(NeuronScope scope)
    {
        Scopes.Add(scope);
        return Task.FromResult<ISalesforceApiClient>(new FakeSalesforceApiClient());
    }

    private sealed class FakeSalesforceApiClient : ISalesforceApiClient
    {
        public Task<string[]> QueryAsync(string soql, CancellationToken ct) => Task.FromResult(Array.Empty<string>());

        public Task<string[]> ListAccountsAsync(int maxResults, CancellationToken ct) =>
            Task.FromResult(new[] { "Acme Test Corp" });
    }
}

