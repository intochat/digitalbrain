# E2E Testing Architecture — Live Neuron Visualization

**Date:** 2026-04-11
**Status:** Design
**Scope:** Three-tier testing architecture for ino — from framework through neurons to the live product

## Problem

Today's 468 tests prove the **framework** works. None of them prove the **product** works.

The test infrastructure is fragmented across four disconnected layers with no coherent architecture:

| Layer | Where | What it tests | What's missing |
|---|---|---|---|
| Neuron isolation | `domains/travel/Ino.Travel.Tests/` | Individual tool methods via TestCluster | No LLM composition, no routing, no gRPC |
| Timeline E2E | `test/E2E.Tests/TimetravelE2ETests.cs` | Shell neuron → timeline events | No travel, no RFW, no gRPC boundary |
| RFW templates | `domains/travel/Ino.Travel/UI/` | **Never tested, never called** | Not wired into InoService |
| Flutter rendering | `ino.flutter/lib/ui/` | Only manual visual check | No golden tests, no automation |

Critical gaps:
1. `InoService.RouteTravelAsync` returns text reply but **never populates `rfw_description`/`rfw_data`** on `ChatResponse` — the proto fields exist (fields 3-4), Flutter's `_RfwContent` widget is wired and functional, but the pipeline between them is broken.
2. `PromptMatchingMockChatClient` returns text only — it cannot simulate tool-calling (`FunctionCallContent` in MEAI), so TravelRecommender multi-neuron composition is untestable.
3. No test ever hits the gRPC boundary — every test calls grain methods directly via `GrainFactory.GetGrain<T>()`, meaning InoService routing and RFW wiring are completely untested.
4. The existing `test/E2E.Tests/` uses raw `TestCluster` with custom silo configurators per test class. There is no shared fixture, no Aspire TestingHost, and no path from TestCluster to gRPC.
5. `iaw/Testing/` provides `NeuronBddContext`, `MockChatClient`, `PromptMatchingMockChatClient` — but has no story for Aspire-managed testing, gRPC, or RFW validation.

## Vision

```
dotnet test --filter FlightSearch
```

One command:
1. Aspire TestingHost boots the full topology: Orleans silo + gRPC server + mocked LLM + mocked SerpApi
2. Flutter web opens in a browser (served from the gRPC server's `wwwroot/`, same as production)
3. Neurons fire, synapses flow — the developer **watches the Flutter UI live**
4. Test asserts on the gRPC response: correct RFW bytes, correct data fields, correct neuron activations
5. Test passes or fails

Every neuron is testable from day 0 by inheriting a single base class. The E2E tests with mocked LLM + mocked SerpApi aren't just tests — they're the development playground. You build ino by running them.

## Three-tier testing architecture

The 468 tests today are all effectively Tier 1-2. This design adds Tier 3 — the tier that proves the product works.

### Tier 1: IAW Framework — "does the platform work?"

| | |
|---|---|
| **Purpose** | Prove the Agent base class, tool discovery, scheduling, streams, communication primitives work |
| **Where** | `test/Core.Tests/`, `test/Integration.Tests/` |
| **Infrastructure** | `iaw/Testing/` + `TestCluster` directly |
| **Mock level** | `MockChatClient` (text responses only) |
| **Proves** | Orleans grains activate, tools register, scheduling fires, streams deliver |
| **Example** | `AgentTests.cs` — 14 test classes verifying agent lifecycle, tool registration, state persistence |

These are regression safety nets. They catch framework-level breakage (someone changes `Agent.Tools.cs` and tool discovery stops working). They do NOT prove any user-facing behavior.

### Tier 2: Neuron Architecture — "does each neuron's contract work?"

| | |
|---|---|
| **Purpose** | Prove each neuron's tool methods return correct data for given inputs |
| **Where** | `domains/*/Tests/`, `features/*/Tests/` |
| **Infrastructure** | `iaw/Testing/` + `NeuronBddContext` (shared TestCluster via `IClassFixture`) |
| **Mock level** | `PromptMatchingMockChatClient` + domain mocks (e.g., `MockSerpApiProvider`) |
| **Proves** | `FlightSearchNeuron.SearchFlights("JFK", "DPS", ...)` returns correct JSON |
| **Convention** | Gherkin `.feature` file per neuron, one scenario per synapse verb, `[Fact]` wrappers |
| **Example** | `FlightSearchScenarioTests.cs` — calls `SearchFlights` directly, asserts JSON structure |

These prove each neuron's API contract works in isolation. They do NOT prove neurons compose, routing works, or the UI renders.

### Tier 3: E2E Product — "does ino work?" (NEW)

| | |
|---|---|
| **Purpose** | Prove the complete user experience: gRPC request → routing → neuron composition → RFW → Flutter |
| **Where** | `test/E2E.Tests/` with `test/E2E.AppHost/` |
| **Infrastructure** | `iaw/Testing/` + Aspire TestingHost (via `AddIAWTesting`) |
| **Mock level** | `ToolCallingMockChat` (simulates LLM tool-calling) + domain mocks |
| **Proves** | "find flights to Bali" → gRPC → TravelRecommender → FlightSearch → FlightCard RFW → Flutter renders |
| **Convention** | Inherit `NeuronE2ETest`, use `ChatAsync()` + `AssertRfw()` |
| **Value** | **These tests ARE the product.** Run them → you're running ino. Mocked LLM + mocked SerpApi = instant, deterministic, zero API cost. The development playground. |

### How the tiers relate

```
Tier 1: IAW Framework          Tier 2: Neuron Architecture       Tier 3: E2E Product
┌─────────────────────┐        ┌─────────────────────────┐       ┌──────────────────────────┐
│ Agent base class     │        │ FlightSearchNeuron      │       │ gRPC Chat("flights")     │
│ Tool discovery       │        │ HotelSearchNeuron       │       │   → InoService routing   │
│ Scheduling           │        │ PlaceDiscoveryNeuron    │       │   → TravelRecommender    │
│ Streams              │        │ PriceTrackerNeuron      │       │     → SearchFlights tool  │
│ State persistence    │        │ TripVaultNeuron         │       │     → FlightSearchNeuron  │
│ Communication        │        │ UserNeuron              │       │     → MockSerpApi         │
│                      │        │ TravelRecommenderNeuron │       │   → RFW FlightCard        │
│ TestCluster          │        │ NeuronBddContext        │       │   → Flutter renders live  │
│ MockChatClient       │        │ PromptMatchingMock      │       │                            │
│                      │        │ MockSerpApiProvider     │       │ Aspire TestingHost         │
│ Regression safety    │        │ Contract correctness    │       │ ToolCallingMockChat        │
│ net                  │        │                         │       │ InoTestHost + NeuronE2ETest│
└─────────────────────┘        └─────────────────────────┘       └──────────────────────────┘
    proves platform                proves each neuron               proves the product
```

Each tier builds on the previous. Tier 3 exercises Tier 1 and 2 code through the real production path. If Tier 3 passes, the product works. If Tier 1 or 2 catches something Tier 3 doesn't, it's a regression safety net.

## `iaw/Testing/` — the canonical testing toolkit

All three tiers use `iaw/Testing/`. Instead of bypassing `AddIAW`, we **extend** the testing library with proper testing counterparts that mirror the production extensions.

### Current state of `iaw/Testing/`

| Component | Serves | Status |
|---|---|---|
| `MockChatClient` | Tier 1 | Exists — text responses, `ReturnsText()`, `ThrowsOnSend()` |
| `PromptMatchingMockChatClient` | Tier 2 | Exists — prompt matchers, text responses |
| `NeuronBddContext` | Tier 2 | Exists — shared TestCluster, prompt-matching mock, scenario state |
| `NeuronBddSiloConfigurator` | Tier 2 | Exists — silo config for BDD tests |
| `MockEmbeddingGenerator` | Tier 1-2 | Exists |
| `AgentTestClientConfigurator` | Tier 1-2 | Exists |

### New additions to `iaw/Testing/`

| Component | Serves | Purpose |
|---|---|---|
| **`AddIAWTesting()`** | Tier 3 | Aspire hosting extension — Orleans in-memory, no containers. Returns `IAWService` so `WithReference(iaw)` works unchanged |
| **`AddIAWSiloTesting()`** | Tier 3 | Silo-side extension — in-memory durable jobs, mock LLM, mock embedding. Replaces `AddIAW()` silo extension in Testing environment |
| **`AddIAWClientTesting()`** | Tier 3 | Client-side extension — Orleans client without blob/qdrant DI. Replaces `AddIAWClient()` in Testing environment |
| **`ToolCallingMockChat`** | Tier 3 | `IChatClient` that returns `FunctionCallContent` for LLM tool-calling simulation |
| **`InoTestHost`** | Tier 3 | `IClassFixture` wrapper for Aspire TestingHost — starts once, shared across all E2E tests |
| **`NeuronE2ETest`** | Tier 3 | Base class all neuron E2E tests inherit |
| **`MockSerpApiProvider`** | Tier 2-3 | Moved from `Ino.Travel.Tests/` — canned SerpApi responses |
| **`MockAirportValidator`** | Tier 2-3 | Moved from `Ino.Travel.Tests/` — city-to-IATA resolution |

### `AddIAWTesting` — mirrors production `AddIAW` without containers

The production `AddIAW` (`iaw/Aspire.Hosting/IAWHostingExtensions.cs`) creates Azure Storage + Qdrant containers. `AddIAWTesting` mirrors the same topology with in-memory resources:

```csharp
// iaw/Testing/IAWTestingHostingExtensions.cs
public static class IAWTestingHostingExtensions
{
    public static IAWService AddIAWTesting(
        this IDistributedApplicationBuilder builder, string name = "iaw")
    {
        var orleans = builder.AddOrleans(name)
            .WithClusterId("test")
            .WithServiceId("test")
            .WithDevelopmentClustering()
            .WithMemoryGrainStorage("Default")
            .WithMemoryGrainStorage("PubSubStore")
            .WithMemoryStreaming(IAWConstants.StreamProvider)
            .WithMemoryReminders();

        // Same IAWService type as production — WithReference(iaw) works unchanged
        return new IAWService(orleans, builder);
        // No Azure Storage, no Qdrant, no secret parameters, zero containers
    }

    // Slimmed-down WithReference that skips blob/qdrant/LLM env vars
    public static IResourceBuilder<T> WithTestReference<T>(
        this IResourceBuilder<T> builder, IAWService iaw)
        where T : IResourceWithEnvironment, IResourceWithEndpoints, IResourceWithWaitSupport
    {
        builder.WithReference(iaw.Orleans);
        return builder;
    }

    public static IResourceBuilder<T> WithTestClientReference<T>(
        this IResourceBuilder<T> builder, IAWService iaw)
        where T : IResourceWithEnvironment, IResourceWithEndpoints, IResourceWithWaitSupport
    {
        builder.WithReference(iaw.Orleans.AsClient());
        return builder;
    }
}
```

### `AddIAWSiloTesting` — mirrors production silo config with mocks

The production `AddIAW<TBuilder>()` (`iaw/Aspire.Client/IAWSiloExtensions.cs`) adds Azure Blob durable jobs, real LLM providers, qdrant, blob storage. `AddIAWSiloTesting` replaces all external dependencies with in-memory/mock equivalents:

```csharp
// iaw/Testing/IAWTestingSiloExtensions.cs
public static class IAWTestingSiloExtensions
{
    public static TBuilder AddIAWSiloTesting<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();

        builder.UseOrleans(silo =>
        {
            silo.Configure<Orleans.Configuration.EndpointOptions>(ep =>
                ep.AdvertisedIPAddress = System.Net.IPAddress.Loopback);
            silo.Configure<Orleans.Configuration.SiloMessagingOptions>(msg =>
                msg.ResponseTimeout = TimeSpan.FromMinutes(5));
            silo.Services.AddSingleton<IStateMachineStorageProvider,
                VolatileStateMachineStorageProvider>();
            silo.AddStateMachineStorage();
            silo.AddBroadcastChannel(IAWConstants.UIBroadcastProvider);
            silo.UseInMemoryDurableJobs(); // instead of Azure Blob durable jobs
        });

        // Mock LLM with tool-calling support
        var mockLlm = new ToolCallingMockChat();
        LlmAttributeMapperRegistration.RegisterAllAttributeMappers(builder.Services, mockLlm);
        builder.Services.AddSingleton<IChatClient>(mockLlm);
        builder.Services.AddSingleton(mockLlm); // typed access for test assertions
        builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
            new MockEmbeddingGenerator());

        // No blob storage, no qdrant, no real LLM providers
        builder.Services.AddSingleton<IawMemoryProvider>();
        builder.Services.AddSingleton<IMemoryLookup>(
            sp => sp.GetRequiredService<IawMemoryProvider>());
        builder.Services.AddHttpClient();
        builder.Services.AddSingleton<Octokit.IGitHubClient>(
            new Octokit.GitHubClient(new Octokit.ProductHeaderValue("iaw-e2e")));

        return builder;
    }
}
```

### `AddIAWClientTesting` — mirrors production client config without external deps

```csharp
// iaw/Testing/IAWTestingClientExtensions.cs
public static class IAWTestingClientExtensions
{
    public static TBuilder AddIAWClientTesting<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();

        builder.UseOrleansClient(client =>
        {
            client.UseLocalhostClustering(clusterId: "test", serviceId: "test");
            client.Configure<Orleans.Configuration.ClientMessagingOptions>(msg =>
                msg.ResponseTimeout = TimeSpan.FromMinutes(5));
        });

        builder.Services.AddSingleton<IClientConnectionRetryFilter,
            GatewayConnectionRetryFilter>();
        // No blob storage, no qdrant references
        return builder;
    }
}
```

### Relationship between production and testing extensions

| Production extension | Testing counterpart | What changes |
|---|---|---|
| `AddIAW()` hosting | `AddIAWTesting()` | No Azure Storage, no Qdrant containers, no secret parameters |
| `WithReference(iaw)` hosting | `WithTestReference(iaw)` | Skips blob/qdrant/LLM env vars, just Orleans |
| `AddIAW<TBuilder>()` silo | `AddIAWSiloTesting()` | In-memory durable jobs, mock LLM, no blob/qdrant/real providers |
| `AddIAWClient<TBuilder>()` client | `AddIAWClientTesting()` | Same Orleans client, no blob/qdrant references |

The testing extensions return the **same types** as production (`IAWService`, `IAWClientService`). The rest of the code doesn't know it's in a test.

## Architecture

### Test AppHost — `test/E2E.AppHost/AppHost.cs`

Uses `AddIAWTesting` from `iaw/Testing/`. Reads like the production AppHost but slimmer:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var iaw = builder.AddIAWTesting("iaw"); // iaw/Testing extension — zero containers

var assistant = builder.AddProject<Projects.Agents_Host>("assistant")
    .WithTestReference(iaw)
    .WithEndpoint("orleans-gateway", e => { e.IsProxied = false; e.Port = 30000; })
    .WithEndpoint("orleans-silo", e => { e.IsProxied = false; e.Port = 11111; });

var telegram = builder.AddProject<Projects.Telegram>("telegram")
    .WithTestClientReference(iaw)
    .WaitFor(assistant);

builder.Build().Run();
```

### Production code — minimal environment checks

The production projects detect Testing environment and call the testing extensions instead:

**`iaw/Agents.Host/Program.cs`** — one branch:
```csharp
if (builder.Environment.IsEnvironment("Testing"))
{
    builder.AddIAWSiloTesting(); // from iaw/Testing — mocks everything
    builder.Services.AddSingleton<ISerpApiProviderService>(new MockSerpApiProvider());
    builder.Services.AddSingleton<IAirportValidationService>(new MockAirportValidator());
}
else
{
    builder.AddIAW(); // production — real everything
    builder.Services.AddTravelDomain(builder.Configuration, builder.Environment);
}

// Both paths share:
builder.UseOrleans(silo =>
{
    silo.AddStartupTask<AgentRegistrationStartupTask>();
    silo.AddTimelineCapture();
    silo.AddInoNew();
});
```

**`iaw/Telegram/Program.cs`** — one branch:
```csharp
if (builder.Environment.IsEnvironment("Testing"))
{
    builder.AddIAWClientTesting(); // from iaw/Testing
    builder.Services.AddGrpc();
    builder.Services.AddSingleton<IAudioTranscriptionService, NoOpTranscriptionService>();
}
else
{
    builder.AddIAWClient(); // production
    builder.Services.AddGrpc();
    builder.AddAzureBlobServiceClient("file-storage");
    builder.Services.Configure<TelegramBotOptions>(builder.Configuration.GetSection("Telegram"));
    builder.Services.AddSingleton<ITelegramBotClient>(...);
    // ... all Telegram services ...
    builder.Services.AddHostedService<StreamSubscriber>();
    builder.Services.AddHostedService<WebhookSetupService>();
}

// Both paths share:
app.UseGrpcWeb();
app.UseStaticFiles();
app.MapGrpcService<InoService>().EnableGrpcWeb();
```

The gRPC endpoint + static file serving (Flutter web) always starts. Only Telegram-specific services (webhook, bot, stream subscriber) are skipped in Testing.

### Why Flutter web "just works"

The production Telegram project already serves Flutter web from `wwwroot/` and hosts gRPC on the same port. Flutter connects to `${Uri.base.origin}` (same origin). When Aspire TestingHost starts the Telegram project and a browser opens its URL, the full Flutter UI automatically connects to the test cluster. No URL configuration needed.

### The base class: `NeuronE2ETest`

Every neuron E2E test inherits this. Zero boilerplate.

```csharp
public abstract class NeuronE2ETest : IClassFixture<InoTestHost>
{
    protected InoTestHost Host { get; }
    protected Ino.InoClient Grpc { get; }
    protected ToolCallingMockChat MockLlm { get; }

    // Full pipeline: gRPC Chat → InoService routing → neurons → RFW
    protected Task<ChatResponse> ChatAsync(string message, string? userId = null);

    // Direct grain access for deeper assertions (scheduling, state, events)
    protected T GetGrain<T>(string id) where T : IGrainWithStringKey;

    // RFW assertion helpers
    protected void AssertRfw(ChatResponse r, string widgetName,
        params (string field, object value)[] expectedData);
    protected void AssertRfwList(ChatResponse r, string widgetName, int minItems);
    protected string GetRfwDescription(ChatResponse r); // UTF-8 decode
    protected JsonElement GetRfwData(ChatResponse r);    // JSON parse
}
```

### The shared fixture: `InoTestHost`

Created **once**, shared across **all** E2E test classes via `IClassFixture<InoTestHost>`. Aspire stack starts once, all tests reuse the same cluster.

```csharp
public sealed class InoTestHost : IAsyncLifetime
{
    public DistributedApplication App { get; private set; }
    public ToolCallingMockChat MockLlm { get; private set; }

    public async ValueTask InitializeAsync()
    {
        var builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.E2E_AppHost>(["--environment=Testing"]);
        App = await builder.BuildAsync();
        await App.StartAsync();

        // opt-in: open Flutter web in browser for live visualization
        if (Environment.GetEnvironmentVariable("INO_E2E_OPEN_BROWSER") == "true")
        {
            var telegramEndpoint = App.GetEndpoint("telegram", "http");
            Process.Start(new ProcessStartInfo(telegramEndpoint.ToString())
                { UseShellExecute = true });
        }
    }

    public Ino.InoClient CreateGrpcClient() =>
        new(GrpcChannel.ForAddress(App.GetEndpoint("telegram", "http")));

    public IClusterClient GetClusterClient() =>
        App.Services.GetRequiredService<IClusterClient>();

    public async ValueTask DisposeAsync() => await App.DisposeAsync();
}
```

### ToolCallingMockChat — the key enabler

The current `PromptMatchingMockChatClient` returns text responses only. For composition neurons (TravelRecommender calling FlightSearch, HotelSearch, PlaceDiscovery in parallel), the mock must return `FunctionCallContent` responses — the MEAI tool-calling protocol.

```csharp
public sealed class ToolCallingMockChat : IChatClient
{
    // Register a tool-calling scenario: return FunctionCallContent for this tool
    public ToolCallingMockChat OnToolCall(string toolName,
        params (string param, object value)[] args);

    // Register a multi-tool scenario (parallel tool calls in one response)
    public ToolCallingMockChat OnMultiToolCall(
        params (string toolName, (string param, object value)[] args)[] calls);

    // Register a text fallback (after tools execute, return summary text)
    public ToolCallingMockChat WithFinalResponse(string text);

    // Track what was called for assertions
    public IReadOnlyList<string> CalledTools { get; }
    public int ToolCallCount { get; }

    // Reset state between tests
    public void Reset();
}
```

**How it works with the Agent framework:**
1. TravelRecommenderNeuron calls `GetResponse("find flights to Bali")`
2. Agent.cs calls `_agent.RunStreamingAsync(...)` which calls `IChatClient.GetResponseAsync(...)`
3. `ToolCallingMockChat` returns `ChatResponse` with `FunctionCallContent { Name = "SearchFlights", Arguments = {...} }`
4. The Agent framework's tool-calling middleware sees the `FunctionCallContent`, finds the matching `AITool` from `GetAllTools()` (registered via `RegisterGrainTools<IFlightSearch>` in `TravelRecommenderNeuron.DefineTools()`), and calls the real grain method
5. `FlightSearchNeuron.SearchFlights(...)` executes with `MockSerpApiProvider`, returns structured JSON
6. The framework feeds the tool result back to the mock → mock returns final text summary
7. `InoService.RouteTravelAsync` receives the structured JSON, calls RFW template builder, populates proto fields

This is the same tool-calling loop the real LLM uses. The mock just deterministically picks the right tools instead of reasoning about it.

### InoService RFW wiring — closing the pipeline gap

`InoService.RouteTravelAsync` currently returns text only (line 50: "future RFW integration point"). Wire the RFW template builders:

```csharp
async Task<ChatResponse> RouteTravelAsync(string message, string userId, CancellationToken ct)
{
    var grainId = string.IsNullOrEmpty(userId) ? "travel-default" : $"travel-{userId}";
    var recommender = clusterClient.GetGrain<ITravelRecommender>(grainId);
    var reply = await recommender.GetResponse(message, ct);

    var response = new ChatResponse
    {
        Reply = reply,
        NeuronId = "TravelRecommender",
        ContentType = "text"
    };

    if (TryBuildRfw(reply, out var rfwDesc, out var rfwData))
    {
        response.RfwDescription = ByteString.CopyFrom(rfwDesc);
        response.RfwData = ByteString.CopyFrom(rfwData);
        response.ContentType = "travel_results";
    }

    return response;
}
```

The `TryBuildRfw` helper parses the JSON `type` field and dispatches to the correct template builder:
- `"flight_results"` → `FlightCardTemplate.BuildList(data)`
- `"hotel_results"` → `HotelCardTemplate.BuildList(data)`
- `"place_results"` → `PlaceCardTemplate.BuildList(data)`
- `"destination_results"` → `DestinationCardTemplate.BuildList(data)`
- `"event_results"` → text-only (no RFW template yet)

## Test scenarios — travel domain

### FlightSearchE2E

```csharp
public class FlightSearchE2E(InoTestHost host) : NeuronE2ETest(host)
{
    [Fact]
    public async Task FindFlights_RendersFlightCards()
    {
        MockLlm.OnToolCall("SearchFlights",
            ("from", "JFK"), ("to", "DPS"), ("departureDate", "2026-07-15"));

        var response = await ChatAsync("find flights from NYC to Bali in July");

        AssertRfw(response, "FlightCard",
            ("airline", "Singapore Airlines"),
            ("from", "JFK"),
            ("to", "DPS"),
            ("price", 450));
    }

    [Fact]
    public async Task ExploreDestinations_RendersDestinationCards()
    {
        MockLlm.OnToolCall("ExploreDestinations", ("from", "JFK"));

        var response = await ChatAsync("where can I fly from New York");

        AssertRfwList(response, "FlightCard", minItems: 2);
    }
}
```

### HotelSearchE2E

```csharp
public class HotelSearchE2E(InoTestHost host) : NeuronE2ETest(host)
{
    [Fact]
    public async Task FindHotels_RendersHotelCards()
    {
        MockLlm.OnToolCall("SearchHotels",
            ("location", "Bali"), ("checkIn", "2026-07-15"), ("checkOut", "2026-07-25"));

        var response = await ChatAsync("find hotels in Bali for July 15-25");

        AssertRfw(response, "HotelCard",
            ("name", "Grand Hyatt Bali"),
            ("price", 180),
            ("rating", 4.5));
    }
}
```

### TripPlanningE2E (multi-neuron composition)

```csharp
public class TripPlanningE2E(InoTestHost host) : NeuronE2ETest(host)
{
    [Fact]
    public async Task PlanTrip_CallsMultipleNeurons()
    {
        MockLlm.OnMultiToolCall(
            ("SearchFlights", new[] { ("from", (object)"JFK"), ("to", "NRT"),
                ("departureDate", "2026-07-15") }),
            ("SearchHotels", new[] { ("location", (object)"Tokyo"),
                ("checkIn", "2026-07-15"), ("checkOut", "2026-07-25") }),
            ("FindPlaces", new[] { ("location", (object)"Tokyo"),
                ("type", (object)"restaurant") }));
        MockLlm.WithFinalResponse(
            "Here's your Tokyo trip plan with flights, hotels, and restaurants.");

        var response = await ChatAsync(
            "plan a trip to Tokyo in July — flights, hotels, and things to do");

        Assert.Equal(3, MockLlm.ToolCallCount);
        Assert.Contains("SearchFlights", MockLlm.CalledTools);
        Assert.Contains("SearchHotels", MockLlm.CalledTools);
        Assert.Contains("FindPlaces", MockLlm.CalledTools);
    }
}
```

### PriceTrackerE2E (scheduling lifecycle)

```csharp
public class PriceTrackerE2E(InoTestHost host) : NeuronE2ETest(host)
{
    [Fact]
    public async Task TrackFlight_SchedulesRecurringCheck()
    {
        var tracker = GetGrain<IPriceTracker>("e2e-tracker");

        var result = await tracker.TrackFlight(
            "JFK", "DPS", "2026-07-15", null, 450m, CancellationToken.None);
        var doc = JsonDocument.Parse(result);
        Assert.Equal("tracking_started",
            doc.RootElement.GetProperty("type").GetString());

        var tracked = await tracker.GetTrackedPrices(CancellationToken.None);
        var trackedDoc = JsonDocument.Parse(tracked);
        Assert.True(trackedDoc.RootElement.GetProperty("data").GetArrayLength() > 0);

        var trackingId = doc.RootElement
            .GetProperty("data").GetProperty("trackingId").GetString()!;
        var stopped = await tracker.StopTracking(trackingId, CancellationToken.None);
        Assert.Equal("tracking_stopped",
            JsonDocument.Parse(stopped).RootElement.GetProperty("type").GetString());
    }
}
```

### NeuronDiscoveryE2E (registry)

```csharp
public class NeuronDiscoveryE2E(InoTestHost host) : NeuronE2ETest(host)
{
    [Fact]
    public async Task TravelNeurons_RegisteredInRegistry()
    {
        var registry = GetGrain<IAgentRegistry>("global");
        var travelNeurons = await registry.ListByDomainAsync(
            "travel", CancellationToken.None);

        Assert.True(travelNeurons.Count >= 7);
        var names = travelNeurons.Select(n => n.DisplayName).ToList();
        Assert.Contains("FlightSearch", names);
        Assert.Contains("HotelSearch", names);
        Assert.Contains("PlaceDiscovery", names);
        Assert.Contains("PriceTracker", names);
        Assert.Contains("TripVault", names);
        Assert.Contains("User", names);
        Assert.Contains("TravelRecommender", names);
    }

    [Fact]
    public async Task HybridSearch_FindsFlightNeuron()
    {
        var registry = GetGrain<IAgentRegistry>("global");
        var results = await registry.HybridSearchAsync(
            "flights", null, CancellationToken.None);

        Assert.Contains(results, r => r.DisplayName == "FlightSearch");
    }
}
```

## File structure

```
iaw/
  Testing/
    Testing.csproj                        ← shared testing toolkit (all 3 tiers)
    MockChatClient.cs                     ← Tier 1: simple text mock (existing)
    NeuronBddHooks.cs                     ← Tier 2: NeuronBddContext, PromptMatchingMock (existing)
    MockEmbeddingGenerator.cs             ← Tier 1-2: (existing)
    ToolCallingMockChat.cs                ← Tier 3: FunctionCallContent-aware mock (NEW)
    IAWTestingHostingExtensions.cs        ← Tier 3: AddIAWTesting, WithTestReference (NEW)
    IAWTestingSiloExtensions.cs           ← Tier 3: AddIAWSiloTesting (NEW)
    IAWTestingClientExtensions.cs         ← Tier 3: AddIAWClientTesting (NEW)
    InoTestHost.cs                        ← Tier 3: Aspire TestingHost fixture (NEW)
    NeuronE2ETest.cs                      ← Tier 3: base class for all E2E tests (NEW)
    Mocks/
      MockSerpApiProvider.cs              ← Tier 2-3: moved from Ino.Travel.Tests (MOVED)
      MockAirportValidator.cs             ← Tier 2-3: moved from Ino.Travel.Tests (MOVED)
      NoOpTranscriptionService.cs         ← Tier 3: stub for Telegram testing (NEW)

test/
  Core.Tests/                             ← Tier 1: IAW framework (existing)
  Integration.Tests/                      ← Tier 1: IAW framework (existing)
  E2E.AppHost/
    E2E.AppHost.csproj                    ← Tier 3: Aspire AppHost for testing (NEW)
    AppHost.cs                            ← uses AddIAWTesting from iaw/Testing
    Properties/
      launchSettings.json                 ← ASPNETCORE_ENVIRONMENT=Testing
  E2E.Tests/
    E2E.Tests.csproj                      ← Tier 3: xunit.v3 + Aspire.Hosting.Testing
    Travel/
      FlightSearchE2E.cs                  ← (NEW)
      HotelSearchE2E.cs                   ← (NEW)
      PlaceDiscoveryE2E.cs                ← (NEW)
      TripPlanningE2E.cs                  ← (NEW)
      PriceTrackerE2E.cs                  ← (NEW)
      NeuronDiscoveryE2E.cs               ← (NEW)
    TimetravelE2ETests.cs                 ← existing, migrated to InoTestHost
    CodeOrchestrationE2ETests.cs          ← existing, migrated to InoTestHost

domains/
  travel/
    Ino.Travel.Tests/                     ← Tier 2: neuron BDD (existing, mocks moved out)
      TravelTestFixture.cs                ← updated to import mocks from iaw/Testing
      Scenarios/                          ← existing neuron contract tests

features/
  ino-new/InoNew.Tests/                   ← Tier 2: cortex, neuron, synapse BDD
  timetravel/Timetravel.Tests/            ← Tier 2: timeline, universe BDD
```

## Production code changes required

| Change | File | What |
|---|---|---|
| Wire RFW templates | `iaw/Telegram/Services/InoService.cs` | `TryBuildRfw` dispatches to template builders, populates `rfw_description`/`rfw_data` on `ChatResponse` |
| Testing environment branch | `iaw/Telegram/Program.cs` | When Testing: call `AddIAWClientTesting()`, skip webhook/bot/blob. When production: unchanged |
| Testing environment branch | `iaw/Agents.Host/Program.cs` | When Testing: call `AddIAWSiloTesting()`, register mock SerpApi/airport. When production: unchanged |

The production code paths are **completely unchanged**. The Testing branches call extensions from `iaw/Testing/`.

## The reusable pattern: how any domain adds E2E tests

### Step 1: Mock data provider
Implement `ISomeDomainService` that returns canned data. Add to `iaw/Testing/Mocks/`. Register in the Testing branch of `Agents.Host/Program.cs`.

### Step 2: Tool-calling scenario
In the test, call `MockLlm.OnToolCall("ToolName", args...)` to tell the mock which tools to call.

### Step 3: One [Fact] per scenario
Inherit `NeuronE2ETest`, use `ChatAsync()` + `AssertRfw()`.

### Step 4: RFW template builder (if neuron has UI)
Add a static template class in the domain's `UI/` folder. Register the type dispatch in `InoService.TryBuildRfw`.

### Step 5: Flutter widget library (if neuron has custom rendering)
Add a `LocalWidgetLibrary` in `ino.flutter/lib/ui/components/` and register it in `createInoRuntime()`.

Steps 4-5 are only needed for neurons with visual output. Steps 1-3 are mandatory for all neurons.

## Performance target

- Aspire TestingHost + silo startup: ~10-15 seconds (one-time, shared via IClassFixture)
- Individual test execution: < 2 seconds per scenario
- Full travel E2E suite (6+ tests): < 30 seconds total
- Existing E2E tests migrated to InoTestHost: same or faster (shared cluster instead of per-class)

## Open questions (decided)

1. **Bypass vs extend `AddIAW`?** → Extend. `iaw/Testing/` gets `AddIAWTesting`, `AddIAWSiloTesting`, `AddIAWClientTesting` that mirror production extensions with in-memory/mock equivalents. Production code unchanged. Testing extensions return the same types so the rest of the code doesn't know it's in a test.

2. **Where do mock services live?** → `iaw/Testing/Mocks/`. The `MockSerpApiProvider` and `MockAirportValidator` move from `domains/travel/Ino.Travel.Tests/` to `iaw/Testing/Mocks/` so both Tier 2 BDD tests and Tier 3 E2E tests share them. `ToolCallingMockChat` goes directly in `iaw/Testing/`.

3. **Browser auto-open: always or opt-in?** → Opt-in via environment variable `INO_E2E_OPEN_BROWSER=true`. CI runs headless. Local dev sets the var to watch live. Default: off (so CI doesn't break).

4. **How does ToolCallingMockChat know which tools to call?** → Explicit registration per test. The mock does NOT try to parse prompts. Each `[Fact]` calls `MockLlm.OnToolCall(...)` for the exact tool-call response. Deterministic and CI-safe. The mock resets between tests.

5. **Three tiers vs one big test project?** → Three tiers with clear purposes. Tier 1 (IAW framework) and Tier 2 (neuron architecture) are regression safety nets. Tier 3 (E2E product) proves the product works and serves as the development playground. Each tier uses `iaw/Testing/` as the shared toolkit.
