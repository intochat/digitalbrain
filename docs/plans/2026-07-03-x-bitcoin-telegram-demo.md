# X → Bitcoin Price → Telegram Demo Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prove DigitalBrain's plain-English "build or modify DigitalBrain" story with one concrete, reliable, well-tested cross-channel automation: a simulated X post from a watched author triggers a real Bitcoin price lookup and a Telegram alert.

**Architecture:** Three small additions wired entirely through existing infrastructure — a Kernel-side `MarketDataNeuron` (real HTTP call to CoinGecko, same shape as `LlmResponderNeuron`), a hand-authored `IPackBehavior` demo pack (same shape as `KeywordWatcherNeuron`/`TelegramResponderNeuron`, seeded in `MarketplaceSeeds.cs`), and a `simulate_x_post` MCP tool (thin wrapper over the existing `IIngressNeuron`). No new closed-loop machinery, no isolated-ino project, no config-store wiring — the whole flow is `Signal` broadcasts through the existing dispatch/embodiment/egress pipeline.

**Tech Stack:** .NET (net11.0), Orleans grains, `HttpClient` (CoinGecko public API, no auth), xUnit + Reqnroll over a real `TestCluster` (`DigitalBrain.TestKit`/`NeuronTestBase`).

## Global Constraints

- Target framework: net11.0. No `Version="*"` in any `.csproj` — this plan adds no new NuGet packages, so `Directory.Packages.props` is untouched.
- No vacuous `/// <summary>` comments; self-explanatory naming over comments. Small inline comments only where genuinely non-obvious (e.g. why a field must be mutable for Reqnroll step ordering).
- Run only the relevant tests per task (`dotnet test --filter "FullyQualifiedName~<Name>"`), not the full suite, per `AGENTS.md`'s "no undefined high-severity rituals" rule. Run `dotnet build` before every test run.
- Follow the spec's non-goals: no real X/Twitter API integration, no live-generation-via-`run_code_foundry` for the demo path.

---

## File Structure

| File | Responsibility |
|---|---|
| `DigitalBrain.Kernel/Market/IMarketDataApiClient.cs` | Create — wrapper interface, same shape as `DigitalBrain.Google`'s `I*ApiClient` pattern. |
| `DigitalBrain.Kernel/Market/CoinGeckoApiClient.cs` | Create — real implementation, plain `HttpClient` call to CoinGecko's public price endpoint. |
| `DigitalBrain.Kernel/Market/MarketDataNeuron.cs` | Create — Kernel-side grain, same shape as `LlmResponderNeuron`: reacts to `Signal("CheckBitcoinPrice")`, broadcasts `Signal("BitcoinPriceChecked")`. |
| `DigitalBrain.Kernel/Program.cs` | Modify — register `IMarketDataApiClient`/`CoinGeckoApiClient` via `AddHttpClient`. |
| `DigitalBrain.Tests/TestSupport/FakeMarketDataApiClient.cs` | Create — deterministic test double with a mutable `Price` property (Reqnroll steps mutate it after the silo/DI container is already built). |
| `DigitalBrain.Tests/TestSupport/TestGrainFactory.cs` | Create — `IGrainFactory` adapter over `NeuronTestBase.Grain<T>`, extracted from `DigitalBrainToolsTests` so both it and the new Reqnroll steps can share one implementation. |
| `DigitalBrain.Tests/Market/MarketDataNeuronTests.cs` | Create — fast `NeuronTestBase` test for the grain in isolation. |
| `DigitalBrain.Mcp.Tools/DigitalBrainMutationTools.cs` | Modify — add `simulate_x_post` tool. |
| `DigitalBrain.Tests/Mcp/DigitalBrainToolsTests.cs` | Modify — use the extracted `TestGrainFactory`; add a test for `simulate_x_post`. |
| `DigitalBrain.Core/MarketplaceSeeds.cs` | Modify — add `XBitcoinTelegramDemoPackCode` const + a `NeuroPack` entry in `LocalUiPacks`. |
| `DigitalBrain.Tests/Distribution/XBitcoinTelegramDemoPackTests.cs` | Create — smoke test proving the seeded pack source compiles, installs, and embodies (reacts to a matching `XPostReceived` with `CheckBitcoinPrice`), without the full downstream chain. |
| `DigitalBrain.Tests/Features/XBitcoinTelegramDemo.feature` | Create — the "play" scenario: full end-to-end proof. |
| `DigitalBrain.Tests/Steps/XBitcoinTelegramDemoSteps.cs` | Create — Reqnroll bindings for the feature, same shape as `TelegramReactiveLoopSteps`. |

---

### Task 1: `IMarketDataApiClient` + `CoinGeckoApiClient`

**Files:**
- Create: `DigitalBrain.Kernel/Market/IMarketDataApiClient.cs`
- Create: `DigitalBrain.Kernel/Market/CoinGeckoApiClient.cs`
- Test: `DigitalBrain.Tests/Market/CoinGeckoApiClientTests.cs`

**Interfaces:**
- Produces: `IMarketDataApiClient.GetBitcoinPriceUsdAsync(CancellationToken ct = default) : Task<string>` — returns a formatted price string like `"$61,234.56"`. Consumed by Task 2's `MarketDataNeuron`.

- [ ] **Step 1: Write the failing test**

```csharp
// DigitalBrain.Tests/Market/CoinGeckoApiClientTests.cs
using System.Net;
using DigitalBrain.Kernel.Market;

namespace DigitalBrain.Tests.Market;

public class CoinGeckoApiClientTests
{
    private sealed class FakeHttpMessageHandler(string jsonResponse) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonResponse, System.Text.Encoding.UTF8, "application/json")
            });
    }

    [Fact]
    public async Task GetBitcoinPriceUsdAsync_parses_coingecko_response_into_formatted_price()
    {
        var handler = new FakeHttpMessageHandler("""{"bitcoin":{"usd":61234.56}}""");
        var httpClient = new HttpClient(handler);
        var client = new CoinGeckoApiClient(httpClient);

        var price = await client.GetBitcoinPriceUsdAsync();

        Assert.Equal("$61,234.56", price);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~CoinGeckoApiClientTests" -v`
Expected: FAIL — `CoinGeckoApiClient` / `IMarketDataApiClient` do not exist (CS0246).

- [ ] **Step 3: Write minimal implementation**

```csharp
// DigitalBrain.Kernel/Market/IMarketDataApiClient.cs
namespace DigitalBrain.Kernel.Market;

public interface IMarketDataApiClient
{
    Task<string> GetBitcoinPriceUsdAsync(CancellationToken ct = default);
}
```

```csharp
// DigitalBrain.Kernel/Market/CoinGeckoApiClient.cs
using System.Text.Json;

namespace DigitalBrain.Kernel.Market;

public sealed class CoinGeckoApiClient(HttpClient httpClient) : IMarketDataApiClient
{
    private const string PriceUrl = "https://api.coingecko.com/api/v3/simple/price?ids=bitcoin&vs_currencies=usd";

    public async Task<string> GetBitcoinPriceUsdAsync(CancellationToken ct = default)
    {
        using var response = await httpClient.GetAsync(PriceUrl, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var usd = doc.RootElement.GetProperty("bitcoin").GetProperty("usd").GetDecimal();
        return "$" + usd.ToString("N2");
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~CoinGeckoApiClientTests" -v`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add DigitalBrain.Kernel/Market/IMarketDataApiClient.cs DigitalBrain.Kernel/Market/CoinGeckoApiClient.cs DigitalBrain.Tests/Market/CoinGeckoApiClientTests.cs
git commit -m "feat(market): add IMarketDataApiClient + real CoinGecko implementation"
```

---

### Task 2: `MarketDataNeuron`

**Files:**
- Create: `DigitalBrain.Kernel/Market/MarketDataNeuron.cs`
- Create: `DigitalBrain.Tests/TestSupport/FakeMarketDataApiClient.cs`
- Modify: `DigitalBrain.Kernel/Program.cs`
- Test: `DigitalBrain.Tests/Market/MarketDataNeuronTests.cs`

**Interfaces:**
- Consumes: `IMarketDataApiClient.GetBitcoinPriceUsdAsync()` (Task 1).
- Produces: grain interface `IMarketDataNeuron : INeuron, IHandle<Signal>`, grain id convention `"market-data-main"` in production (mirrors `"llm-main"`, `"market-main"`). Reacts to `Signal("CheckBitcoinPrice", {chatId, ...})`, broadcasts `Signal("BitcoinPriceChecked", {chatId, ..., price})` — merges all incoming props and adds `price`. Consumed by Task 4's demo pack (via the broadcast stream, not a direct reference).

- [ ] **Step 1: Write the failing test**

```csharp
// DigitalBrain.Tests/TestSupport/FakeMarketDataApiClient.cs
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
```

```csharp
// DigitalBrain.Tests/Market/MarketDataNeuronTests.cs
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~MarketDataNeuronTests" -v`
Expected: FAIL — `IMarketDataNeuron`/`MarketDataNeuron` do not exist (CS0246).

- [ ] **Step 3: Write minimal implementation**

```csharp
// DigitalBrain.Kernel/Market/MarketDataNeuron.cs
using DigitalBrain.Core;

namespace DigitalBrain.Kernel.Market;

public interface IMarketDataNeuron : INeuron, IHandle<Signal> { }

[GrainType("digitalbrain.market-data")]
public class MarketDataNeuron(ILogger<MarketDataNeuron> logger, NeuronJournals journals, IMarketDataApiClient client)
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
```

Modify `DigitalBrain.Kernel/Program.cs` — register the real client next to the other capability singletons (after the existing `RoslynAnalysisService` registration):

```csharp
// Before:
builder.Services.AddSingleton<DigitalBrain.Developer.RoslynAnalysisService>();

// After:
builder.Services.AddSingleton<DigitalBrain.Developer.RoslynAnalysisService>();
builder.Services.AddHttpClient<DigitalBrain.Kernel.Market.IMarketDataApiClient, DigitalBrain.Kernel.Market.CoinGeckoApiClient>();
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~MarketDataNeuronTests" -v`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add DigitalBrain.Kernel/Market/MarketDataNeuron.cs DigitalBrain.Kernel/Program.cs DigitalBrain.Tests/TestSupport/FakeMarketDataApiClient.cs DigitalBrain.Tests/Market/MarketDataNeuronTests.cs
git commit -m "feat(market): add MarketDataNeuron reacting to CheckBitcoinPrice"
```

---

### Task 3: `simulate_x_post` MCP tool

**Files:**
- Create: `DigitalBrain.Tests/TestSupport/TestGrainFactory.cs`
- Modify: `DigitalBrain.Mcp.Tools/DigitalBrainMutationTools.cs`
- Modify: `DigitalBrain.Tests/Mcp/DigitalBrainToolsTests.cs`

**Interfaces:**
- Consumes: existing `IIngressNeuron.IngestAsync(string signalName, IReadOnlyDictionary<string, object?> props)` (`DigitalBrain.Kernel/Gateway/IngressNeuron.cs`), grain id `"ingress-main"`.
- Produces: MCP tool `simulate_x_post(author, text, chatId)`, broadcasts `Signal("XPostReceived", {author, text, chatId})`. Consumed by Task 5's Reqnroll scenario (and the live demo).

- [ ] **Step 1: Write the failing test**

First, extract the existing private `TestGrainFactory` (currently nested inside `DigitalBrainToolsTests`) into a shared file so the new Reqnroll steps class can reuse it instead of duplicating an `IGrainFactory` adapter:

```csharp
// DigitalBrain.Tests/TestSupport/TestGrainFactory.cs
using DigitalBrain.TestKit;
using Orleans.Runtime;

namespace DigitalBrain.Tests.TestSupport;

// Adapts NeuronTestBase.Grain<T>() to IGrainFactory for MCP tool classes (constructed directly, outside
// the cluster) that only ever resolve string-keyed grains.
public sealed class TestGrainFactory(NeuronTestBase owner) : IGrainFactory
{
    public TGrainInterface GetGrain<TGrainInterface>(string primaryKey, string? grainClassNamePrefix = null)
        where TGrainInterface : IGrainWithStringKey => owner.Grain<TGrainInterface>(primaryKey);

    public TGrainInterface GetGrain<TGrainInterface>(string primaryKey, string keyExtension, string? grainClassNamePrefix = null)
        where TGrainInterface : IGrainWithStringKey => owner.Grain<TGrainInterface>(primaryKey);

    public TGrainInterface GetGrain<TGrainInterface>(Guid primaryKey, string? grainClassNamePrefix = null) where TGrainInterface : IGrainWithGuidKey => throw new NotSupportedException("Only string-keyed grains for MCP tests");
    public TGrainInterface GetGrain<TGrainInterface>(long primaryKey, string? grainClassNamePrefix = null) where TGrainInterface : IGrainWithIntegerKey => throw new NotSupportedException("Only string-keyed grains for MCP tests");
    public TGrainInterface GetGrain<TGrainInterface>(Guid primaryKey, string keyExtension, string? grainClassNamePrefix = null) where TGrainInterface : IGrainWithGuidCompoundKey => throw new NotSupportedException("Only string-keyed grains for MCP tests");
    public TGrainInterface GetGrain<TGrainInterface>(long primaryKey, string keyExtension, string? grainClassNamePrefix = null) where TGrainInterface : IGrainWithIntegerCompoundKey => throw new NotSupportedException("Only string-keyed grains for MCP tests");

    public IGrain GetGrain(Type grainInterfaceType, Guid grainPrimaryKey) => throw new NotSupportedException();
    public IGrain GetGrain(Type grainInterfaceType, long grainPrimaryKey) => throw new NotSupportedException();
    public IGrain GetGrain(Type grainInterfaceType, string grainPrimaryKey) => throw new NotSupportedException();
    public IGrain GetGrain(Type grainInterfaceType, Guid grainPrimaryKey, string? keyExtension = null) => throw new NotSupportedException();
    public IGrain GetGrain(Type grainInterfaceType, long grainPrimaryKey, string? keyExtension = null) => throw new NotSupportedException();
    public IGrain GetGrain(Type grainInterfaceType, string grainPrimaryKey, string? keyExtension = null) => throw new NotSupportedException();

    public TGrainInterface GetGrain<TGrainInterface>(GrainId grainId) where TGrainInterface : IAddressable => throw new NotSupportedException();
    public IAddressable GetGrain(GrainId grainId) => throw new NotSupportedException();
    public IAddressable GetGrain(GrainId grainId, GrainInterfaceType interfaceType) => throw new NotSupportedException();
    public IAddressable GetGrain(Type interfaceType, IdSpan grainKey, string? grainClassNamePrefix = null) => throw new NotSupportedException();
    public IAddressable GetGrain(Type interfaceType, IdSpan grainKey) => throw new NotSupportedException();

    public TGrainObserverInterface CreateObjectReference<TGrainObserverInterface>(IGrainObserver obj) where TGrainObserverInterface : IGrainObserver => throw new NotSupportedException();
    public void DeleteObjectReference<TGrainObserverInterface>(IGrainObserver obj) where TGrainObserverInterface : IGrainObserver => throw new NotSupportedException();
}
```

Modify `DigitalBrain.Tests/Mcp/DigitalBrainToolsTests.cs` to use the shared factory instead of its own nested copy, and add the new test:

```csharp
using DigitalBrain.Core;
using DigitalBrain.Mcp.Tools;
using DigitalBrain.Tests.TestSupport;
using DigitalBrain.TestKit;

namespace DigitalBrain.Tests.Mcp;

// The MCP tools are co-hosted in the silo and resolve grains via an in-process IGrainFactory.
// These tests exercise that exact path (TestCluster grain factory) without an HTTP transport.
public class DigitalBrainToolsTests : NeuronTestBase
{
    [Fact]
    public void Ping_Works_Standalone()
        => Assert.Contains("connected", DigitalBrainReadTools.PingDigitalBrain(), System.StringComparison.OrdinalIgnoreCase);

    [Fact]
    public async Task Publish_Then_List_Through_InProcess_GrainFactory()
    {
        var factory = new TestGrainFactory(this);
        var mutationTools = new DigitalBrainMutationTools(factory);
        var readTools = new DigitalBrainReadTools(factory);

        await mutationTools.PublishToMarketplace("McpPack", "1.0", "public class P {}", "mcp-user", false, 0.15);
        var listing = await readTools.ListMarketplace();

        Assert.Contains("McpPack@1.0", listing);
    }

    [Fact]
    public async Task SimulateXPost_broadcasts_XPostReceived_signal()
    {
        var factory = new TestGrainFactory(this);
        var mutationTools = new DigitalBrainMutationTools(factory);

        await mutationTools.SimulateXPost("elon", "big news", 7);

        var ingress = Grain<IIngressNeuron>("ingress-main");
        Signal? signal = null;
        for (var attempt = 0; attempt < 20 && signal is null; attempt++)
        {
            await Task.Delay(50);
            var timeline = await ingress.GetOutgoingTimelineAsync();
            signal = timeline.OfType<Signal>().FirstOrDefault(s => s.Name == "XPostReceived");
        }

        Assert.NotNull(signal);
        Assert.Equal("elon", signal!.Props["author"]);
        Assert.Equal("big news", signal.Props["text"]);
        Assert.Equal(7L, signal.Props["chatId"]);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~DigitalBrainToolsTests" -v`
Expected: FAIL — `SimulateXPost` does not exist on `DigitalBrainMutationTools` (CS1061), and/or `IGrainWithStringKey`/`Guid` overload ambiguity if `TestGrainFactory` isn't extracted correctly. Also expect a build error until the nested `TestGrainFactory` is removed from `DigitalBrainToolsTests.cs` (duplicate type conflict is avoided by deleting the private nested class in this same step).

- [ ] **Step 3: Write minimal implementation**

Add to `DigitalBrain.Mcp.Tools/DigitalBrainMutationTools.cs`, right after the existing `fire_synapse` tool:

```csharp
[McpServerTool(Name = "simulate_x_post"), Description("Simulate a new X (Twitter) post from an author, for demo/testing automations that react to XPostReceived. No real X API call is made.")]
public async Task<string> SimulateXPost(
    [Description("X handle/author of the simulated post, e.g. 'elon'")] string author,
    [Description("Post text")] string text,
    [Description("Telegram chat id to notify if a reactive automation replies")] long chatId)
{
    var ingress = Grains.GetGrain<IIngressNeuron>("ingress-main");
    await ingress.IngestAsync("XPostReceived",
        new Dictionary<string, object?> { ["author"] = author, ["text"] = text, ["chatId"] = chatId });
    return $"Simulated X post from '{author}' broadcast as XPostReceived (chatId {chatId}).";
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~DigitalBrainToolsTests" -v`
Expected: PASS (both `Publish_Then_List_Through_InProcess_GrainFactory` and the new `SimulateXPost_broadcasts_XPostReceived_signal`)

- [ ] **Step 5: Commit**

```bash
git add DigitalBrain.Tests/TestSupport/TestGrainFactory.cs DigitalBrain.Tests/Mcp/DigitalBrainToolsTests.cs DigitalBrain.Mcp.Tools/DigitalBrainMutationTools.cs
git commit -m "feat(mcp): add simulate_x_post tool, extract shared TestGrainFactory"
```

---

### Task 4: Demo pack source in `MarketplaceSeeds.cs`

**Files:**
- Modify: `DigitalBrain.Core/MarketplaceSeeds.cs`
- Test: `DigitalBrain.Tests/Distribution/XBitcoinTelegramDemoPackTests.cs`

**Interfaces:**
- Produces: `MarketplaceSeeds.XBitcoinTelegramDemoPackCode` (const string), and a `NeuroPack` entry named `"DigitalBrain.Experience.XBitcoinTelegramDemo"` v`"1.0.0"` in `LocalUiPacks`. Consumed by Task 5's Reqnroll scenario (publishes/installs this exact pack name+version+code).
- Consumes: `Signal("XPostReceived")` (Task 3), produces `Signal("CheckBitcoinPrice")` (consumed by Task 2's `MarketDataNeuron`) and `Signal("TelegramReplyRequested")` (existing Telegram egress path, `DigitalBrain.Telegram/Synapses.cs`).

- [ ] **Step 1: Write the failing test**

```csharp
// DigitalBrain.Tests/Distribution/XBitcoinTelegramDemoPackTests.cs
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.TestKit;

namespace DigitalBrain.Tests.Distribution;

public sealed class XBitcoinTelegramDemoPackTests : NeuronTestBase
{
    private const string PackName = "DigitalBrain.Experience.XBitcoinTelegramDemo";

    [Fact]
    public async Task Installed_pack_reacts_to_matching_XPostReceived_with_CheckBitcoinPrice()
    {
        var market = Grain<IMarketplaceNeuron>("market-demo-pack-smoke");
        await market.FireAsync(new PublishToMarketplace(
            PackName, "1.0.0", Code: MarketplaceSeeds.XBitcoinTelegramDemoPackCode,
            OwnerId: "tester", IsPrivate: false, CommissionRate: 0.0));
        await market.FireAsync(new InstallFromMarketplace(PackName, "1.0.0", BuyerId: "smoke-test-user"));

        var ingress = Grain<IIngressNeuron>("ingress-smoke");
        await ingress.IngestAsync("XPostReceived",
            new Dictionary<string, object?> { ["author"] = "elon", ["text"] = "big news", ["chatId"] = 7L });

        var gen = Grain<IGeneratedNeuron>("generated-" + PackName.ToLowerInvariant());
        Signal? checkPrice = null;
        for (var attempt = 0; attempt < 40 && checkPrice is null; attempt++)
        {
            await Task.Delay(50);
            var timeline = await gen.GetOutgoingTimelineAsync();
            checkPrice = timeline.OfType<Signal>().FirstOrDefault(s => s.Name == "CheckBitcoinPrice");
        }

        Assert.NotNull(checkPrice);
        Assert.Equal(7L, checkPrice!.Props["chatId"]);
        Assert.Equal("elon", checkPrice.Props["author"]);
    }

    [Fact]
    public async Task Installed_pack_ignores_XPostReceived_from_an_unwatched_author()
    {
        var market = Grain<IMarketplaceNeuron>("market-demo-pack-smoke-2");
        await market.FireAsync(new PublishToMarketplace(
            PackName, "1.0.0", Code: MarketplaceSeeds.XBitcoinTelegramDemoPackCode,
            OwnerId: "tester", IsPrivate: false, CommissionRate: 0.0));
        await market.FireAsync(new InstallFromMarketplace(PackName, "1.0.0", BuyerId: "smoke-test-user-2"));

        var ingress = Grain<IIngressNeuron>("ingress-smoke-2");
        await ingress.IngestAsync("XPostReceived",
            new Dictionary<string, object?> { ["author"] = "someone_else", ["text"] = "irrelevant", ["chatId"] = 9L });

        var gen = Grain<IGeneratedNeuron>("generated-" + PackName.ToLowerInvariant());
        await Task.Delay(300);
        var timeline = await gen.GetOutgoingTimelineAsync();
        Assert.DoesNotContain(timeline.OfType<Signal>(), s => s.Name == "CheckBitcoinPrice");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~XBitcoinTelegramDemoPackTests" -v`
Expected: FAIL — `MarketplaceSeeds.XBitcoinTelegramDemoPackCode` does not exist (CS0117).

- [ ] **Step 3: Write minimal implementation**

Add to `DigitalBrain.Core/MarketplaceSeeds.cs`, after `KeywordWatcherPackCode`'s closing `""";`:

```csharp
    public const string XBitcoinTelegramDemoPackCode = """
using System.Collections.Generic;
using DigitalBrain.Core;

public sealed class XBitcoinTelegramDemoNeuron : IPackBehavior
{
    private const string WatchedAuthor = "elon";

    public PackManifest GetManifest() => new(
        new[] { new SynapseType("XPostReceived"), new SynapseType("BitcoinPriceChecked") },
        null);

    public string Respond(string input) => input;

    public IReadOnlyList<Synapse> Handle(Synapse synapse)
    {
        if (synapse is Signal xPost && xPost.Name == "XPostReceived")
        {
            var author = xPost.Props.TryGetValue("author", out var a) ? a?.ToString() ?? "" : "";
            if (!string.Equals(author, WatchedAuthor, System.StringComparison.OrdinalIgnoreCase))
                return System.Array.Empty<Synapse>();

            return new Synapse[]
            {
                new Signal("CheckBitcoinPrice", new Dictionary<string, object?>
                {
                    ["chatId"] = xPost.Props.TryGetValue("chatId", out var c) ? c : null,
                    ["author"] = author
                })
            };
        }

        if (synapse is Signal priceChecked && priceChecked.Name == "BitcoinPriceChecked")
        {
            var author = priceChecked.Props.TryGetValue("author", out var a) ? a?.ToString() ?? WatchedAuthor : WatchedAuthor;
            var price = priceChecked.Props.TryGetValue("price", out var p) ? p?.ToString() ?? "unknown" : "unknown";

            return new Synapse[]
            {
                new Signal("TelegramReplyRequested", new Dictionary<string, object?>
                {
                    ["chatId"] = priceChecked.Props.TryGetValue("chatId", out var c) ? c : null,
                    ["text"] = $"New post from {author}. Bitcoin price right now: {price}"
                })
            };
        }

        return System.Array.Empty<Synapse>();
    }

    public BundleManifest? GetBundleManifest() => new(
        BundleTier.Content,
        null,
        new[] { BundleChannel.Telegram });
}
""";
```

Then add a `NeuroPack` entry to `LocalUiPacks`, after the `KeywordWatcherPackCode` entry:

```csharp
        new NeuroPack(
            "DigitalBrain.Experience.XBitcoinTelegramDemo",
            "1.0.0",
            "digitalbraintech",
            false,
            0.0,
            XBitcoinTelegramDemoPackCode,
            "Demo: reacts to a simulated X post from a watched author, checks the Bitcoin price, and sends a Telegram alert.",
            Manifest: new(BundleTier.Content, null, new[] { BundleChannel.Telegram })),
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~XBitcoinTelegramDemoPackTests" -v`
Expected: PASS (both facts)

- [ ] **Step 5: Commit**

```bash
git add DigitalBrain.Core/MarketplaceSeeds.cs DigitalBrain.Tests/Distribution/XBitcoinTelegramDemoPackTests.cs
git commit -m "feat(marketplace): seed the X-Bitcoin-Telegram demo pack"
```

---

### Task 5: End-to-end Reqnroll scenario

**Files:**
- Create: `DigitalBrain.Tests/Features/XBitcoinTelegramDemo.feature`
- Create: `DigitalBrain.Tests/Steps/XBitcoinTelegramDemoSteps.cs`

**Interfaces:**
- Consumes: everything from Tasks 1-4 (`MarketplaceSeeds.XBitcoinTelegramDemoPackCode`, `IMarketDataNeuron`, `FakeMarketDataApiClient`, `DigitalBrainMutationTools.SimulateXPost`, `TestGrainFactory`).
- Produces: nothing new consumed elsewhere — this is the terminal proof.

- [ ] **Step 1: Write the failing test**

```gherkin
# DigitalBrain.Tests/Features/XBitcoinTelegramDemo.feature
Feature: X post triggers a Bitcoin price alert on Telegram
	As a user
	I want DigitalBrain to react to a new X post from a watched author
	So that it checks the Bitcoin price and sends me a Telegram alert

	@distribution @e2e @xbitcoindemo
	Scenario: X post from watched author triggers a Bitcoin price alert on Telegram
		Given the X-Bitcoin-Telegram demo pack is installed
		And the egress bus is watching "TelegramReplyRequested"
		When a simulated X post from "elon" arrives for chat 7 with text "big news"
		Then a "TelegramReplyRequested" reply for chat 7 with text "New post from elon. Bitcoin price right now: $61,234.56" reaches the egress bus
```

```csharp
// DigitalBrain.Tests/Steps/XBitcoinTelegramDemoSteps.cs
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

        // Force MarketDataNeuron to activate/subscribe before the broadcast chain reaches it (same
        // requirement as LlmResponderNeuron in TelegramReactiveLoopSteps — production startup
        // activation is a pre-existing, accepted gap, not introduced by this task).
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
        Assert.Equal(chatId, Convert.ToInt32(received!.Props["chatId"]));
        Assert.Equal(text, received.Props["text"]);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~XBitcoinTelegramDemo" -v`
Expected: FAIL — either a Reqnroll "no matching step definition" error before Steps 1-4 above exist, or a timeout/assertion failure if any earlier task's wiring is incomplete.

- [ ] **Step 3: Write minimal implementation**

No new production code — this task only wires together Tasks 1-4 through the feature/steps files written in Step 1 above.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~XBitcoinTelegramDemo" -v`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add DigitalBrain.Tests/Features/XBitcoinTelegramDemo.feature DigitalBrain.Tests/Steps/XBitcoinTelegramDemoSteps.cs
git commit -m "test(demo): add end-to-end X -> Bitcoin -> Telegram Reqnroll scenario"
```

---

## Final verification (after all 5 tasks)

```bash
dotnet build Brain.slnx --nologo
dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~Market|FullyQualifiedName~XBitcoinTelegramDemo|FullyQualifiedName~DigitalBrainToolsTests" -v
```

Expected: build succeeds, all tests pass. Full-suite `dotnet test` (no filter) is optional here per `AGENTS.md`'s "run the tests that are relevant" — run it only if you want the extra confidence before merging.
