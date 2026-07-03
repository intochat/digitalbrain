using DigitalBrain.Core;
using DigitalBrain.Core.Ui;
using DigitalBrain.Runtime.Grpc;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Foundry;
using DigitalBrain.Kernel.Gateway;
using DigitalBrain.Kernel.Market;
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
        var payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            prompt = "what's the bitcoin price?",
            sessionId = "chat-session-btc"
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
        Assert.Equal("chat-session-btc", surface.Props["sessionId"]);
        Assert.Equal("assistant", surface.Props["role"]);

        var tree = Assert.IsType<UiWidgetTree>(surface.Props["tree"]);
        Assert.Equal(UiKitVocabulary.Text, tree.Type);
        Assert.Contains("$42,123.45", tree.Props["text"]!.ToString());
    }

    [Fact]
    public async Task Send_SurfaceDemoRequested_InstallsPack_And_BroadcastsRenderableSurface()
    {
        using var subscription = _homeFeedBus.Subscribe();
        var svc = NewService();

        await svc.Send(new SynapseEnvelope
        {
            TypeName = KernelSurfaceDemo.RequestType,
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

        var generated = Grain<IGeneratedNeuron>(KernelSurfaceDemo.GeneratedNeuronKey);
        var timeline = await generated.GetOutgoingTimelineAsync();
        var emittedSurface = Assert.Single(timeline.OfType<UiSurface>(), surface =>
            surface.Props.TryGetValue(UiSurfaceKeys.SurfaceId, out var id) &&
            Equals(id, "surface-demo-pack"));
        Assert.Equal("ui-demo-test", emittedSurface.CorrelationId);
        Assert.False(string.IsNullOrWhiteSpace(emittedSurface.CausationId));

        var observability = Grain<IObservabilityNeuron>(KernelSurfaceDemo.ObservabilityNeuronKey);
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
