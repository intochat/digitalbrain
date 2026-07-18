# ino Tokyo Demo Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a working, screenshot-able demo where a user plans a 5-day Tokyo trip through the ino Flutter web app, with the full synapse/neuron architecture visibly carrying the experience — ino as root neuron, travel capabilities as runtime handlers, itinerary composed at runtime via an L1 Roslyn script, persona projected live from the in-flight synapse graph, persona orb animated via Rive with a CustomPaint fallback.

**Architecture:** See `docs/superpowers/specs/2026-04-13-ino-tokyo-demo-design.md`. Summary: chat RPC fires a synapse at `"ino"`; `InoNeuronHandler` plans intent, fires sub-synapses at travel capability handlers, synthesises the answer; `travel:itinerary-composer` is pure L1 (registry row with Roslyn `ScriptSource` + `RfwTemplateSource`); persona propagates via `personaContext` in every synapse payload; `InoPersonaProjector` derives activity from the live timeline subtree.

**Tech Stack:** .NET 11 / Orleans 10.x / `Microsoft.CodeAnalysis.CSharp.Scripting` / RFW 1.1.3 / Flutter 3.41 / `rive: ^0.14.5` / gRPC / Playwright / xUnit.v3 / Reqnroll (Gherkin) / Aspire 9.

---

## Phase ordering (demo-fast)

1. **Phase A** — Runtime migration: `InoNeuron`, `TravelRecommenderHandler`, flight/hotel/place handlers, chat RPC routes to `"ino"`
2. **Phase C** — `travel:itinerary-composer` L1 (Roslyn script + RFW template script, no handler)
3. **Phase E** — Flutter chat thread append-only + Rive persona orb with fallback
4. **Phase F** — `TokyoTripPlanningE2E` Playwright test covering the full flow

Demo ships after Phase F. Phases **D** (live graph projector + clarification), **G** (polish), **B** (decay consolidation) are scoped at the end as follow-up.

---

## File Structure — MVP phases

### Created

| Path | Responsibility |
|---|---|
| `src/Core/Neurons/Ino/InoNeuronHandler.cs` | `ISynapseHandler` keyed `"ino"` — plans intent, fires child synapse, synthesises response |
| `src/Core/Neurons/Ino/InoActivityMap.cs` | target-neuron-id → activity label table (used by the projector in Phase D; defined now for consistency) |
| `src/Core/Neurons/Ino/PersonaContext.cs` | record for the `personaContext` payload field + (de)serialisation helpers |
| `src/Core/Neurons/Startup/InoRootRegistrationStartupTask.cs` | Orleans startup task that registers `creator`, `ino`, and travel neuron rows in `NeuronRegistryGrain` |
| `src/Core/Neurons/Ino/InoServiceCollectionExtensions.cs` | `AddInoRuntimeNeurons(IServiceCollection)` extension — registers keyed `ISynapseHandler` instances + startup task |
| `domains/travel/Ino.Travel/Handlers/FlightSearchHandler.cs` | `ISynapseHandler` keyed `"travel:flight-search"` — wraps `ISerpApiProviderService` |
| `domains/travel/Ino.Travel/Handlers/HotelSearchHandler.cs` | `ISynapseHandler` keyed `"travel:hotel-search"` — wraps `ISerpApiProviderService` |
| `domains/travel/Ino.Travel/Handlers/PlaceDiscoveryHandler.cs` | `ISynapseHandler` keyed `"travel:place-discovery"` |
| `domains/travel/Ino.Travel/Handlers/TravelRecommenderHandler.cs` | `ISynapseHandler` keyed `"travel:recommender"` — ChatFacade planner that fires child synapses as "tools" |
| `domains/travel/Ino.Travel/Handlers/TravelHandlerPayloads.cs` | Typed request/response records for each travel synapse verb |
| `domains/travel/Ino.Travel/Scripts/ItineraryComposerScript.cs` | static class exposing the Roslyn `ScriptSource` string for `travel:itinerary-composer` |
| `domains/travel/Ino.Travel/Scripts/FlightCardRfwScript.cs` | static class exposing the `RfwTemplateSource` for `travel:flight-search` |
| `domains/travel/Ino.Travel/Scripts/HotelCardRfwScript.cs` | `RfwTemplateSource` for `travel:hotel-search` |
| `domains/travel/Ino.Travel/Scripts/PlaceCardRfwScript.cs` | `RfwTemplateSource` for `travel:place-discovery` |
| `domains/travel/Ino.Travel/Scripts/ItineraryCardRfwScript.cs` | `RfwTemplateSource` for `travel:itinerary-composer` |
| `tests/Core.Tests/Neurons/Ino/InoNeuronHandlerTests.cs` | unit tests with TestCluster + MockChatClient |
| `tests/Core.Tests/Neurons/Ino/PersonaContextTests.cs` | (de)serialisation round-trip tests |
| `tests/E2E.Tests/Travel/TokyoTripPlanningE2E.cs` | full-flow Playwright E2E |
| `clients/ino.flutter/lib/screens/home/chat_message_tile.dart` | per-message widget (text / rfw / clarification / error) |
| `clients/ino.flutter/lib/persona/rive_persona_orb.dart` | real Rive renderer with CustomPaint fallback on load failure |
| `clients/ino.flutter/assets/rive/persona_orb.riv` | Rive asset (placeholder acceptable; fallback renders visually) |

### Modified

| Path | Change |
|---|---|
| `src/Core/Neurons/NeuronGrain.cs` | unchanged; specialist handler lookup already in place |
| `src/Core/Contracts/SynapseResult.cs` | add `NeedsClarification(question, options?)` factory (used later in Phase D but reserved in Phase A to avoid churn) |
| `src/Telegram/Program.cs` | `Chat` RPC fires single synapse at `"ino"`, maps result to `ChatResponse`; `RouteTravelAsync` deleted |
| `Aspire/ino.Client/IAWSiloExtensions.cs` (or the silo DI registration site) | call `services.AddInoRuntimeNeurons()` |
| `domains/travel/Ino.Travel/Ino.Travel.csproj` | ensure reference to `src/Core/Core.csproj` is present (it already is) |
| `domains/travel/Ino.Travel/UI/FlightCardTemplate.cs` | delete (replaced by `FlightCardRfwScript`) |
| `domains/travel/Ino.Travel/UI/HotelCardTemplate.cs` | delete (replaced by `HotelCardRfwScript`) |
| `domains/travel/Ino.Travel/UI/PlaceCardTemplate.cs` | delete (replaced by `PlaceCardRfwScript`) |
| `domains/travel/Ino.Travel/UI/DestinationCardTemplate.cs` | delete (unused after migration) |
| `tests/E2E.Tests/Travel/FlightSearchE2E.cs` | update `NeuronId` assertion to `"ino"`; add timeline assertion for `ino → travel:recommender → travel:flight-search` |
| `tests/E2E.Tests/Travel/HotelSearchE2E.cs` | same pattern |
| `tests/E2E.Tests/Travel/PlaceDiscoveryE2E.cs` | same pattern |
| `tests/E2E.Tests/Travel/PriceTrackerE2E.cs` | same pattern |
| `tests/E2E.Tests/Travel/TripPlanningE2E.cs` | same pattern + add partial RFW assertion |
| `tests/E2E.Tests/Travel/NeuronDiscoveryE2E.cs` | updated assertions for `creator` and `ino` registry rows |
| `clients/ino.flutter/lib/screens/home/home_screen.dart` | guaranteed append-only `ListView` of `ChatMessageTile` widgets with slide-up entrance animation |
| `clients/ino.flutter/lib/persona/persona_widget.dart` | `_RivePlaceholder` replaced with `RivePersonaOrb` (graceful fallback) |
| `clients/ino.flutter/lib/state/ino_bloc.dart` | message append path verified; clarification reply-to-correlation wiring reserved for Phase D |

---

## Phase A — InoNeuron + travel handler migration

### Task A0: Context7 verification (pre-code gate)

**Files:** none modified — research only

- [ ] **Step 1: Load Context7 and Microsoft Learn tool schemas**

Run (via `ToolSearch`):
```
select:mcp__context7__resolve-library-id,mcp__context7__query-docs,mcp__microsoft-learn__microsoft_docs_search,mcp__microsoft-learn__microsoft_docs_fetch
```

- [ ] **Step 2: Resolve Orleans library id and query the exact APIs we'll use**

```
mcp__context7__resolve-library-id(libraryName="Microsoft.Orleans")
mcp__context7__query-docs(
    libraryId=<resolved>,
    query="IPersistentState IGrainFactory GetKeyedService ISynapseHandler pattern keyed DI registration")
mcp__context7__query-docs(
    libraryId=<resolved>,
    query="Orleans 10 startup task IHostedService register grain silo extension")
```

Expected: confirm keyed-service DI pattern is `services.AddKeyedSingleton<ISynapseHandler, FlightSearchHandler>("travel:flight-search")` and that `GrainFactory.GetGrain<INeuron>(id)` is correct for id-keyed grains.

- [ ] **Step 3: Verify `Microsoft.CodeAnalysis.CSharp.Scripting` script globals binding**

```
mcp__context7__resolve-library-id(libraryName="Microsoft.CodeAnalysis.CSharp.Scripting")
mcp__context7__query-docs(libraryId=<resolved>, query="CSharpScript.Create globals ScriptOptions.Default AddReferences AddImports ScriptRunner<T>")
```

Expected: confirm the `ScriptRunner<SynapseResult>` pattern with `NeuronScriptGlobals` matches what `src/Core/Neurons/NeuronGrain.cs:261-284` already does so the new itinerary script compiles against the same globals.

- [ ] **Step 4: Verify Flutter `rive: 0.14.5` state machine controller API**

```
mcp__context7__resolve-library-id(libraryName="rive flutter")
mcp__context7__query-docs(libraryId=<resolved>,
    query="RiveAnimation.asset StateMachineController SMIInput SMITrigger onInit artboard load error handling")
```

Expected: confirm the `RiveAnimation.asset(..., onInit: (Artboard) { StateMachineController.fromArtboard(...) })` pattern and `SMIInput<double>`, `SMITrigger` input types for Phase E.

- [ ] **Step 5: Verify `rfw` Dart DSL syntax for the itinerary card** (referenced in Phase C)

```
mcp__context7__resolve-library-id(libraryName="rfw flutter")
mcp__context7__query-docs(libraryId=<resolved>,
    query="parseLibraryFile RemoteWidget DynamicContent widget Column ListView binding data")
```

Expected: confirm the RFW `import widgets; widget root = Column(children: [...]);` DSL is valid for a scrollable list of itinerary days.

- [ ] **Step 6: Note findings inline in the plan doc if any API differs from expectation**

If Context7 surfaces an API difference that invalidates a code block below, update the plan task before proceeding and commit the plan update separately.

---

### Task A1: Create `PersonaContext` record + tests

**Files:**
- Create: `src/Core/Neurons/Ino/PersonaContext.cs`
- Test: `tests/Core.Tests/Neurons/Ino/PersonaContextTests.cs`

- [ ] **Step 1: Write the failing (de)serialisation test**

`tests/Core.Tests/Neurons/Ino/PersonaContextTests.cs`:
```csharp
using System.Text.Json;
using Core.Neurons.Ino;
using Xunit;

namespace Core.Tests.Neurons.Ino;

public sealed class PersonaContextTests
{
    [Fact]
    public void WriteAndReadRoundTrips_PreservesAllFields()
    {
        var context = new PersonaContext(
            Snapshot: "{\"traits\":{\"prefers_morning\":\"true\"}}",
            ParentNeuronId: "ino",
            CorrelationId: "corr-42");

        var envelope = PersonaContext.WrapRequest(context, "{\"text\":\"plan tokyo trip\"}");
        Assert.Contains("\"personaContext\"", envelope);
        Assert.Contains("\"request\"", envelope);

        Assert.True(PersonaContext.TryRead(envelope, out var read, out var innerRequest));
        Assert.Equal("ino", read!.ParentNeuronId);
        Assert.Equal("corr-42", read.CorrelationId);
        Assert.Equal("{\"text\":\"plan tokyo trip\"}", innerRequest);
    }

    [Fact]
    public void TryRead_ReturnsFalse_WhenPayloadHasNoPersonaContext()
    {
        Assert.False(PersonaContext.TryRead("{\"foo\":1}", out _, out _));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet test tests/Core.Tests --filter "FullyQualifiedName~PersonaContextTests"
```
Expected: FAIL (class `PersonaContext` does not exist).

- [ ] **Step 3: Create the `PersonaContext` record + helpers**

`src/Core/Neurons/Ino/PersonaContext.cs`:
```csharp
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Core.Neurons.Ino;

public sealed record PersonaContext(
    string Snapshot,
    string ParentNeuronId,
    string CorrelationId)
{
    public static string WrapRequest(PersonaContext context, string requestJson)
    {
        var envelope = new JsonObject
        {
            ["personaContext"] = new JsonObject
            {
                ["snapshot"] = JsonNode.Parse(context.Snapshot) ?? new JsonObject(),
                ["parentNeuronId"] = context.ParentNeuronId,
                ["correlationId"] = context.CorrelationId,
            },
            ["request"] = JsonNode.Parse(requestJson) ?? JsonValue.Create(requestJson),
        };
        return envelope.ToJsonString();
    }

    public static bool TryRead(string envelopeJson, out PersonaContext? context, out string innerRequest)
    {
        context = null;
        innerRequest = string.Empty;

        if (string.IsNullOrWhiteSpace(envelopeJson))
            return false;

        var root = JsonNode.Parse(envelopeJson);
        if (root is not JsonObject obj || obj["personaContext"] is not JsonObject personaNode)
            return false;

        context = new PersonaContext(
            Snapshot: personaNode["snapshot"]?.ToJsonString() ?? "{}",
            ParentNeuronId: personaNode["parentNeuronId"]?.GetValue<string>() ?? "",
            CorrelationId: personaNode["correlationId"]?.GetValue<string>() ?? "");

        innerRequest = obj["request"]?.ToJsonString() ?? "{}";
        return true;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

```
dotnet test tests/Core.Tests --filter "FullyQualifiedName~PersonaContextTests"
```
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Core/Neurons/Ino/PersonaContext.cs tests/Core.Tests/Neurons/Ino/PersonaContextTests.cs
git commit -m "feat(ino): add PersonaContext envelope for synapse payloads"
```

---

### Task A2: Create `InoActivityMap` (target id → activity label table)

**Files:**
- Create: `src/Core/Neurons/Ino/InoActivityMap.cs`

- [ ] **Step 1: Write the file**

`src/Core/Neurons/Ino/InoActivityMap.cs`:
```csharp
namespace Core.Neurons.Ino;

// Canonical mapping from target neuron id to a persona activity label. Used by
// InoPersonaProjector in Phase D to derive what the live subtree is doing.
// Defined now so Phase A handlers, Phase C script, Phase E Flutter client, and
// Phase F E2E tests all agree on a single enum of labels.
public static class InoActivityMap
{
    public const string Idle = "idle";
    public const string Thinking = "thinking";
    public const string ReadingPreferences = "reading_preferences";
    public const string SearchingFlights = "searching_flights";
    public const string SearchingHotels = "searching_hotels";
    public const string SearchingPlaces = "searching_places";
    public const string ComposingItinerary = "composing_itinerary";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";

    public static string FromTargetNeuronId(string targetId) => targetId switch
    {
        "travel:flight-search" => SearchingFlights,
        "travel:hotel-search" => SearchingHotels,
        "travel:place-discovery" => SearchingPlaces,
        "travel:itinerary-composer" => ComposingItinerary,
        "travel:recommender" => Thinking,
        "ino" => Thinking,
        _ => Idle,
    };
}
```

- [ ] **Step 2: Compile to verify it builds**

```
dotnet build src/Core/Core.csproj
```
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/Core/Neurons/Ino/InoActivityMap.cs
git commit -m "feat(ino): add InoActivityMap central target-id to activity table"
```

---

### Task A3: Create travel handler payload records

**Files:**
- Create: `domains/travel/Ino.Travel/Handlers/TravelHandlerPayloads.cs`

- [ ] **Step 1: Create the payload records**

`domains/travel/Ino.Travel/Handlers/TravelHandlerPayloads.cs`:
```csharp
namespace Ino.Travel.Handlers;

// Typed payloads for every travel synapse verb. Serialised/deserialised inside
// handlers. This file is the single source of truth for travel handler DTOs so
// handlers + ItineraryComposerScript (Phase C) agree on shapes.

public sealed record FlightSearchRequest(
    string From,
    string To,
    string DepartureDate,
    string? ReturnDate,
    string? CabinClass,
    int? PassengerCount);

public sealed record FlightResult(
    string Airline,
    string From,
    string To,
    decimal Price,
    string Currency,
    string Date,
    string Duration,
    string? BestForYouReason);

public sealed record FlightSearchResponse(IReadOnlyList<FlightResult> Items);

public sealed record HotelSearchRequest(
    string Location,
    string CheckIn,
    string CheckOut,
    int? Guests,
    string? PriceTier);

public sealed record HotelResult(
    string Name,
    string Location,
    decimal PricePerNight,
    string Currency,
    double Rating,
    double WalkingDistanceToLandmark,
    string? BestForYouReason);

public sealed record HotelSearchResponse(IReadOnlyList<HotelResult> Items);

public sealed record PlaceDiscoveryRequest(
    string Location,
    string? Category);

public sealed record PlaceResult(
    string Name,
    string Category,
    string Neighborhood,
    double Rating,
    string Description);

public sealed record PlaceDiscoveryResponse(IReadOnlyList<PlaceResult> Items);

public sealed record ItineraryRequest(
    string Destination,
    string StartDate,
    int DayCount,
    string? Budget,
    IReadOnlyList<string>? Interests);

public sealed record ItineraryDayEntry(
    string Time,
    string Title,
    string Detail,
    string Kind);

public sealed record ItineraryDay(
    int DayNumber,
    string Date,
    string Headline,
    IReadOnlyList<ItineraryDayEntry> Entries);

public sealed record ItineraryView(
    string Destination,
    string Summary,
    IReadOnlyList<ItineraryDay> Days);
```

- [ ] **Step 2: Compile**

```
dotnet build domains/travel/Ino.Travel/Ino.Travel.csproj
```
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add domains/travel/Ino.Travel/Handlers/TravelHandlerPayloads.cs
git commit -m "feat(travel): add typed payload records for synapse handlers"
```

---

### Task A4: Create `FlightSearchHandler` (ISynapseHandler)

**Files:**
- Create: `domains/travel/Ino.Travel/Handlers/FlightSearchHandler.cs`
- Test: `tests/Core.Tests/Neurons/Ino/FlightSearchHandlerTests.cs`

- [ ] **Step 1: Write the failing handler unit test**

`tests/Core.Tests/Neurons/Ino/FlightSearchHandlerTests.cs`:
```csharp
using System.Text.Json;
using Core.Contracts;
using Core.Neurons;
using Core.Neurons.Ino;
using IAW.Testing;
using Ino.Travel.Handlers;
using Ino.Travel.Services;
using Moq;
using Orleans;
using Xunit;

namespace Core.Tests.Neurons.Ino;

public sealed class FlightSearchHandlerTests
{
    [Fact]
    public async Task HandleAsync_CallsSerpApiAndReturnsPayload()
    {
        var serp = new Mock<ISerpApiProviderService>();
        serp.Setup(s => s.SearchFlightsAsync(
                It.Is<FlightSearchParams>(p => p.From == "NYC" && p.To == "NRT"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FlightSearchResponse(new[]
            {
                new FlightResult("ANA", "NYC", "NRT", 980m, "USD", "2026-05-12", "13h30m", null),
            }));

        var handler = new FlightSearchHandler(serp.Object);

        var request = new FlightSearchRequest("NYC", "NRT", "2026-05-12", "2026-05-17", "economy", 1);
        var envelope = PersonaContext.WrapRequest(
            new PersonaContext("{}", "ino", "corr-1"),
            JsonSerializer.Serialize(request));

        var synapse = new Synapse(
            Id: "s-1", SourceId: "travel:recommender", TargetId: "travel:flight-search",
            Verb: "search_flights", Payload: envelope,
            FiredAt: DateTimeOffset.UtcNow, CorrelationId: "corr-1", Decay: 100);

        var grainFactory = new Mock<IGrainFactory>().Object;
        var result = await handler.HandleAsync(synapse, grainFactory, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("flights.found", result.Verb);
        var decoded = JsonSerializer.Deserialize<FlightSearchResponse>(result.Payload)!;
        Assert.Single(decoded.Items);
        Assert.Equal("ANA", decoded.Items[0].Airline);
    }

    [Fact]
    public async Task HandleAsync_ReturnsFailedResult_WhenSerpThrows()
    {
        var serp = new Mock<ISerpApiProviderService>();
        serp.Setup(s => s.SearchFlightsAsync(It.IsAny<FlightSearchParams>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom"));

        var handler = new FlightSearchHandler(serp.Object);
        var synapse = new Synapse(
            Id: "s-2", SourceId: "travel:recommender", TargetId: "travel:flight-search",
            Verb: "search_flights",
            Payload: PersonaContext.WrapRequest(
                new PersonaContext("{}", "ino", "corr-2"),
                JsonSerializer.Serialize(new FlightSearchRequest("NYC", "NRT", "2026-05-12", null, null, 1))),
            FiredAt: DateTimeOffset.UtcNow, CorrelationId: "corr-2", Decay: 100);

        var result = await handler.HandleAsync(synapse, new Mock<IGrainFactory>().Object, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("error", result.Verb);
        Assert.Contains("boom", result.Payload);
    }
}
```

Note: `FlightSearchParams` is the existing DTO shape inside `ISerpApiProviderService`; exact field names must be cross-checked at execution time by reading `domains/travel/Ino.Travel/Services/ISerpApiProviderService.cs`. If names differ, adjust this block.

- [ ] **Step 2: Run test to verify it fails**

```
dotnet test tests/Core.Tests --filter "FullyQualifiedName~FlightSearchHandlerTests"
```
Expected: FAIL (`FlightSearchHandler` does not exist).

- [ ] **Step 3: Implement the handler**

`domains/travel/Ino.Travel/Handlers/FlightSearchHandler.cs`:
```csharp
using System.Text.Json;
using Core.Contracts;
using Core.Neurons;
using Core.Neurons.Ino;
using Ino.Travel.Services;

namespace Ino.Travel.Handlers;

public sealed class FlightSearchHandler : ISynapseHandler
{
    readonly ISerpApiProviderService _serp;

    public FlightSearchHandler(ISerpApiProviderService serp)
    {
        _serp = serp;
    }

    public async Task<SynapseResult> HandleAsync(
        Synapse synapse,
        IGrainFactory grainFactory,
        CancellationToken ct = default)
    {
        if (!PersonaContext.TryRead(synapse.Payload, out _, out var innerRequest))
            return new SynapseResult(Success: false, Payload: "missing personaContext envelope", Verb: "error");

        var request = JsonSerializer.Deserialize<FlightSearchRequest>(innerRequest);
        if (request is null)
            return new SynapseResult(Success: false, Payload: "invalid FlightSearchRequest", Verb: "error");

        try
        {
            var response = await _serp.SearchFlightsAsync(
                new FlightSearchParams(
                    From: request.From,
                    To: request.To,
                    DepartureDate: request.DepartureDate,
                    ReturnDate: request.ReturnDate,
                    CabinClass: request.CabinClass ?? "economy",
                    Passengers: request.PassengerCount ?? 1),
                ct);

            return new SynapseResult(
                Success: true,
                Payload: JsonSerializer.Serialize(response),
                Verb: "flights.found");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new SynapseResult(Success: false, Payload: ex.Message, Verb: "error");
        }
    }
}
```

Note: Adjust `FlightSearchParams` record/class construction to match the existing `ISerpApiProviderService` signature found in `domains/travel/Ino.Travel/Services/ISerpApiProviderService.cs`. If a direct projection is impossible, introduce a mapping inside the handler.

- [ ] **Step 4: Run tests to verify they pass**

```
dotnet test tests/Core.Tests --filter "FullyQualifiedName~FlightSearchHandlerTests"
```
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add domains/travel/Ino.Travel/Handlers/FlightSearchHandler.cs tests/Core.Tests/Neurons/Ino/FlightSearchHandlerTests.cs
git commit -m "feat(travel): FlightSearchHandler as ISynapseHandler over SerpApi"
```

---

### Task A5: Create `HotelSearchHandler` (mirror A4)

**Files:**
- Create: `domains/travel/Ino.Travel/Handlers/HotelSearchHandler.cs`
- Test: `tests/Core.Tests/Neurons/Ino/HotelSearchHandlerTests.cs`

- [ ] **Step 1: Write the failing test**

Mirror `FlightSearchHandlerTests` structure with:
- `HotelSearchRequest("Tokyo", "2026-05-12", "2026-05-17", 1, "mid_range")`
- Mock `ISerpApiProviderService.SearchHotelsAsync` returning two `HotelResult` items.
- Assert `result.Verb == "hotels.found"` and payload deserialises to `HotelSearchResponse` with two items.

```csharp
using System.Text.Json;
using Core.Contracts;
using Core.Neurons;
using Core.Neurons.Ino;
using Ino.Travel.Handlers;
using Ino.Travel.Services;
using Moq;
using Orleans;
using Xunit;

namespace Core.Tests.Neurons.Ino;

public sealed class HotelSearchHandlerTests
{
    [Fact]
    public async Task HandleAsync_CallsSerpApi_ReturnsHotelPayload()
    {
        var serp = new Mock<ISerpApiProviderService>();
        serp.Setup(s => s.SearchHotelsAsync(It.IsAny<HotelSearchParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HotelSearchResponse(new[]
            {
                new HotelResult("Andaz Tokyo", "Toranomon", 450m, "USD", 4.7, 0.8, null),
                new HotelResult("Park Hyatt Tokyo", "Shinjuku", 650m, "USD", 4.8, 2.1, null),
            }));

        var handler = new HotelSearchHandler(serp.Object);
        var request = new HotelSearchRequest("Tokyo", "2026-05-12", "2026-05-17", 1, "mid_range");
        var envelope = PersonaContext.WrapRequest(
            new PersonaContext("{}", "ino", "corr-h-1"),
            JsonSerializer.Serialize(request));

        var synapse = new Synapse(
            Id: "s-h-1", SourceId: "travel:recommender", TargetId: "travel:hotel-search",
            Verb: "search_hotels", Payload: envelope,
            FiredAt: DateTimeOffset.UtcNow, CorrelationId: "corr-h-1", Decay: 100);

        var result = await handler.HandleAsync(synapse, new Mock<IGrainFactory>().Object, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("hotels.found", result.Verb);
        var decoded = JsonSerializer.Deserialize<HotelSearchResponse>(result.Payload)!;
        Assert.Equal(2, decoded.Items.Count);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet test tests/Core.Tests --filter "FullyQualifiedName~HotelSearchHandlerTests"
```
Expected: FAIL.

- [ ] **Step 3: Implement `HotelSearchHandler`**

Same shape as `FlightSearchHandler` — decode the persona-context envelope, parse `HotelSearchRequest`, call `_serp.SearchHotelsAsync`, return a `SynapseResult` with verb `"hotels.found"`.

```csharp
using System.Text.Json;
using Core.Contracts;
using Core.Neurons;
using Core.Neurons.Ino;
using Ino.Travel.Services;

namespace Ino.Travel.Handlers;

public sealed class HotelSearchHandler : ISynapseHandler
{
    readonly ISerpApiProviderService _serp;

    public HotelSearchHandler(ISerpApiProviderService serp) => _serp = serp;

    public async Task<SynapseResult> HandleAsync(
        Synapse synapse,
        IGrainFactory grainFactory,
        CancellationToken ct = default)
    {
        if (!PersonaContext.TryRead(synapse.Payload, out _, out var innerRequest))
            return new SynapseResult(false, "missing personaContext envelope", "error");

        var request = JsonSerializer.Deserialize<HotelSearchRequest>(innerRequest);
        if (request is null)
            return new SynapseResult(false, "invalid HotelSearchRequest", "error");

        try
        {
            var response = await _serp.SearchHotelsAsync(
                new HotelSearchParams(
                    Location: request.Location,
                    CheckIn: request.CheckIn,
                    CheckOut: request.CheckOut,
                    Guests: request.Guests ?? 1,
                    PriceTier: request.PriceTier ?? "mid_range"),
                ct);

            return new SynapseResult(true, JsonSerializer.Serialize(response), "hotels.found");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new SynapseResult(false, ex.Message, "error");
        }
    }
}
```

- [ ] **Step 4: Run test to pass, commit**

```
dotnet test tests/Core.Tests --filter "FullyQualifiedName~HotelSearchHandlerTests"
git add domains/travel/Ino.Travel/Handlers/HotelSearchHandler.cs tests/Core.Tests/Neurons/Ino/HotelSearchHandlerTests.cs
git commit -m "feat(travel): HotelSearchHandler as ISynapseHandler"
```

---

### Task A6: Create `PlaceDiscoveryHandler` (mirror A4)

**Files:**
- Create: `domains/travel/Ino.Travel/Handlers/PlaceDiscoveryHandler.cs`
- Test: `tests/Core.Tests/Neurons/Ino/PlaceDiscoveryHandlerTests.cs`

- [ ] **Step 1-5: Follow Task A4/A5 pattern**

Payload: `PlaceDiscoveryRequest(Location, Category?)` → `PlaceDiscoveryResponse(Items)`. Verb on success: `"places.found"`. Underlying service call: `_serp.SearchPlacesAsync` or the equivalent signature discovered at execution time in `domains/travel/Ino.Travel/Services/ISerpApiProviderService.cs`.

Commit message: `feat(travel): PlaceDiscoveryHandler as ISynapseHandler`.

---

### Task A7: Create `TravelRecommenderHandler` — planner that fires child synapses

**Files:**
- Create: `domains/travel/Ino.Travel/Handlers/TravelRecommenderHandler.cs`
- Test: `tests/Core.Tests/Neurons/Ino/TravelRecommenderHandlerTests.cs`

- [ ] **Step 1: Write the failing test using TestCluster + ToolCallingMockChat**

`tests/Core.Tests/Neurons/Ino/TravelRecommenderHandlerTests.cs`:
```csharp
using System.Text.Json;
using Core.Contracts;
using Core.Neurons;
using Core.Neurons.Ino;
using IAW.Testing;
using Ino.Travel.Handlers;
using Xunit;

namespace Core.Tests.Neurons.Ino;

[Collection(nameof(InoTestCluster))]
public sealed class TravelRecommenderHandlerTests : AgentTest<ITravelRecommender>
{
    [Fact]
    public async Task HandleAsync_FiresFlightHotelAndPlaceSynapses_InOneTurn()
    {
        MockChat.OnMultiToolCall(
            ("fire_flight_search", [("from","NYC"),("to","Tokyo"),("departureDate","2026-05-12"),("returnDate",(object)"2026-05-17")]),
            ("fire_hotel_search",  [("location","Tokyo"),("checkIn","2026-05-12"),("checkOut","2026-05-17")]),
            ("fire_place_discovery",[("location","Tokyo"),("category",(object)"food_market")]))
            .WithFinalResponse("Here is a Tokyo trip plan with food markets.");

        var ino = TestCluster.Client.GetGrain<INeuron>("travel:recommender");

        var request = JsonSerializer.Serialize(new { text = "plan 5 days in Tokyo mid-May food markets" });
        var envelope = PersonaContext.WrapRequest(
            new PersonaContext("{\"traits\":{\"prefers_food_markets\":\"true\"}}", "ino", "corr-tr-1"),
            request);

        var synapse = new Synapse(
            Id: "s-tr-1", SourceId: "ino", TargetId: "travel:recommender",
            Verb: "plan_trip", Payload: envelope,
            FiredAt: DateTimeOffset.UtcNow, CorrelationId: "corr-tr-1", Decay: 100);

        var result = await ino.HandleAsync(synapse);

        Assert.True(result.Success);
        Assert.Equal(3, MockChat.ToolCallCount);
        Assert.Contains("fire_flight_search", MockChat.CalledTools);
        Assert.Contains("fire_hotel_search",  MockChat.CalledTools);
        Assert.Contains("fire_place_discovery", MockChat.CalledTools);
        Assert.Contains("Tokyo", result.Payload);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet test tests/Core.Tests --filter "FullyQualifiedName~TravelRecommenderHandlerTests"
```
Expected: FAIL.

- [ ] **Step 3: Implement `TravelRecommenderHandler` — ChatFacade planner with tools-as-synapses**

`domains/travel/Ino.Travel/Handlers/TravelRecommenderHandler.cs`:
```csharp
using System.Text.Json;
using Core.Contracts;
using Core.Neurons;
using Core.Neurons.Ino;
using Core.Neurons.Runtime;
using Microsoft.Extensions.AI;

namespace Ino.Travel.Handlers;

public sealed class TravelRecommenderHandler : ISynapseHandler
{
    readonly IChatClient _chat;

    public TravelRecommenderHandler(IChatClient chat) => _chat = chat;

    public async Task<SynapseResult> HandleAsync(
        Synapse synapse,
        IGrainFactory grainFactory,
        CancellationToken ct = default)
    {
        if (!PersonaContext.TryRead(synapse.Payload, out var personaContext, out var innerRequest))
            return new SynapseResult(false, "missing personaContext envelope", "error");

        var tools = new[]
        {
            AIFunctionFactory.Create(
                name: "fire_flight_search",
                description: "Fire a synapse at travel:flight-search for a flight search request.",
                method: async (string from, string to, string departureDate, string? returnDate, CancellationToken fnCt) =>
                {
                    var request = new FlightSearchRequest(from, to, departureDate, returnDate, null, 1);
                    return await FireChildAsync(
                        grainFactory, "travel:flight-search", "search_flights",
                        request, personaContext!, synapse.CorrelationId, fnCt);
                }),
            AIFunctionFactory.Create(
                name: "fire_hotel_search",
                description: "Fire a synapse at travel:hotel-search for a hotel search request.",
                method: async (string location, string checkIn, string checkOut, CancellationToken fnCt) =>
                {
                    var request = new HotelSearchRequest(location, checkIn, checkOut, 1, "mid_range");
                    return await FireChildAsync(
                        grainFactory, "travel:hotel-search", "search_hotels",
                        request, personaContext!, synapse.CorrelationId, fnCt);
                }),
            AIFunctionFactory.Create(
                name: "fire_place_discovery",
                description: "Fire a synapse at travel:place-discovery for a place/attraction search.",
                method: async (string location, string? category, CancellationToken fnCt) =>
                {
                    var request = new PlaceDiscoveryRequest(location, category);
                    return await FireChildAsync(
                        grainFactory, "travel:place-discovery", "search_places",
                        request, personaContext!, synapse.CorrelationId, fnCt);
                }),
        };

        var systemPrompt = BuildSystemPrompt(personaContext!);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, innerRequest),
        };

        var options = new ChatOptions { Tools = tools };
        var completion = await _chat.GetResponseAsync(messages, options, ct);

        return new SynapseResult(
            Success: true,
            Payload: completion.Message.Text ?? string.Empty,
            Verb: "plan.composed");
    }

    static string BuildSystemPrompt(PersonaContext ctx) =>
        $"""
        You are the travel recommender inside the ino system. You never answer flight/hotel/place
        questions yourself — you fire synapses at specialist neurons via the provided tools, then
        synthesise a concise user-facing summary of what you found. Persona snapshot: {ctx.Snapshot}
        """;

    static async Task<string> FireChildAsync<TRequest>(
        IGrainFactory grainFactory,
        string targetNeuronId,
        string verb,
        TRequest request,
        PersonaContext context,
        string correlationId,
        CancellationToken ct)
    {
        var envelope = PersonaContext.WrapRequest(context, JsonSerializer.Serialize(request));
        var synapse = new Synapse(
            Id: Guid.NewGuid().ToString("n"),
            SourceId: "travel:recommender",
            TargetId: targetNeuronId,
            Verb: verb,
            Payload: envelope,
            FiredAt: DateTimeOffset.UtcNow,
            CorrelationId: correlationId,
            Decay: 100);

        var child = grainFactory.GetGrain<INeuron>(targetNeuronId);
        var result = await child.HandleAsync(synapse, ct);
        return result.Payload;
    }
}
```

Note: `AIFunctionFactory.Create` signature belongs to `Microsoft.Extensions.AI`. Confirm exact overloads in Context7 step A0-3. If the overloads differ, adapt the tool construction block.

- [ ] **Step 4: Run test to verify it passes**

```
dotnet test tests/Core.Tests --filter "FullyQualifiedName~TravelRecommenderHandlerTests"
```
Expected: PASS — MockChat fires the three tools, handler invokes the respective child grains (stubbed via TestCluster's DI-wired handlers that are registered in the InoTestCluster fixture — Task A10 will set that fixture up).

If this test requires the InoTestCluster fixture (Task A10) to exist first, reorder and implement A10 before running this test; leave the test file in place but mark as Skip until the fixture lands, then unskip in A10's commit.

- [ ] **Step 5: Commit**

```bash
git add domains/travel/Ino.Travel/Handlers/TravelRecommenderHandler.cs tests/Core.Tests/Neurons/Ino/TravelRecommenderHandlerTests.cs
git commit -m "feat(travel): TravelRecommenderHandler — LLM planner firing child synapses"
```

---

### Task A8: Create `InoNeuronHandler`

**Files:**
- Create: `src/Core/Neurons/Ino/InoNeuronHandler.cs`
- Test: `tests/Core.Tests/Neurons/Ino/InoNeuronHandlerTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Text.Json;
using Core.Contracts;
using Core.Neurons;
using Core.Neurons.Ino;
using IAW.Testing;
using Xunit;

namespace Core.Tests.Neurons.Ino;

[Collection(nameof(InoTestCluster))]
public sealed class InoNeuronHandlerTests : AgentTest<ITravelRecommender>
{
    [Fact]
    public async Task HandleAsync_RoutesTravelQueries_ToTravelRecommender()
    {
        MockChat
            .OnMultiToolCall(
                ("fire_flight_search", [("from","NYC"),("to","Tokyo"),("departureDate","2026-05-12"),("returnDate",(object)"2026-05-17")]),
                ("fire_hotel_search",  [("location","Tokyo"),("checkIn","2026-05-12"),("checkOut","2026-05-17")]),
                ("fire_place_discovery",[("location","Tokyo"),("category",(object)"food_market")]))
            .WithFinalResponse("Here is a Tokyo trip plan with food markets.");

        var ino = TestCluster.Client.GetGrain<INeuron>("ino");

        var userPayload = JsonSerializer.Serialize(new { text = "plan 5 days in Tokyo mid-May food markets" });
        var synapse = new Synapse(
            Id: "s-root-1", SourceId: "chat", TargetId: "ino",
            Verb: "user.request", Payload: userPayload,
            FiredAt: DateTimeOffset.UtcNow, CorrelationId: "corr-root-1", Decay: 100);

        var result = await ino.HandleAsync(synapse);

        Assert.True(result.Success);
        Assert.Contains("Tokyo", result.Payload);
        Assert.Equal(3, MockChat.ToolCallCount);
    }

    [Fact]
    public async Task HandleAsync_NonTravelQuery_ReturnsFallbackResponse()
    {
        MockChat.WithFinalResponse("I can help with travel planning today.");

        var ino = TestCluster.Client.GetGrain<INeuron>("ino");
        var synapse = new Synapse(
            Id: "s-root-2", SourceId: "chat", TargetId: "ino",
            Verb: "user.request",
            Payload: JsonSerializer.Serialize(new { text = "what time is it in paris" }),
            FiredAt: DateTimeOffset.UtcNow, CorrelationId: "corr-root-2", Decay: 100);

        var result = await ino.HandleAsync(synapse);

        Assert.True(result.Success);
        Assert.Contains("travel", result.Payload, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet test tests/Core.Tests --filter "FullyQualifiedName~InoNeuronHandlerTests"
```
Expected: FAIL (InoNeuronHandler + "ino" registration missing).

- [ ] **Step 3: Implement `InoNeuronHandler`**

`src/Core/Neurons/Ino/InoNeuronHandler.cs`:
```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using Core.Contracts;
using Core.Neurons.Runtime;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Core.Neurons.Ino;

// InoNeuronHandler is the root of every chat turn. It reads the user request,
// reads the ino persona state, plans intent (travel vs other), fires a synapse
// at the relevant capability neuron, and returns the synthesised result. Phase
// A implements travel routing only; other domains return a fallback text.
public sealed class InoNeuronHandler : ISynapseHandler
{
    static readonly string[] TravelKeywords =
    [
        "trip", "travel", "flight", "hotel", "plan", "itinerary",
        "vacation", "getaway", "tokyo", "paris", "visit", "holiday",
    ];

    readonly IChatClient _chat;
    readonly ILogger<InoNeuronHandler> _log;

    public InoNeuronHandler(IChatClient chat, ILogger<InoNeuronHandler> log)
    {
        _chat = chat;
        _log = log;
    }

    public async Task<SynapseResult> HandleAsync(
        Synapse synapse,
        IGrainFactory grainFactory,
        CancellationToken ct = default)
    {
        // 1. Read or construct the persona context. In Phase A we construct a
        //    minimal context — Phase D reads IPersonaGrain and inlines the snapshot.
        var personaContext = new PersonaContext(
            Snapshot: "{}",
            ParentNeuronId: "ino",
            CorrelationId: synapse.CorrelationId);

        // 2. Parse the user request text.
        string userText = ExtractUserText(synapse.Payload);

        // 3. Route by simple keyword intent (upgraded to LLM classifier in Phase D).
        if (IsTravelQuery(userText))
        {
            _log.LogInformation("InoNeuron routing to travel:recommender for correlation={CorrelationId}",
                synapse.CorrelationId);

            var inner = JsonSerializer.Serialize(new { text = userText });
            var envelope = PersonaContext.WrapRequest(personaContext, inner);

            var childSynapse = new Synapse(
                Id: Guid.NewGuid().ToString("n"),
                SourceId: "ino",
                TargetId: "travel:recommender",
                Verb: "plan_trip",
                Payload: envelope,
                FiredAt: DateTimeOffset.UtcNow,
                CorrelationId: synapse.CorrelationId,
                Decay: 100);

            var child = grainFactory.GetGrain<INeuron>("travel:recommender");
            var childResult = await child.HandleAsync(childSynapse, ct);

            return childResult with { Verb = childResult.Verb };
        }

        // 4. Non-travel fallback — brief chat response via LLM (Phase D replaces
        //    with a real intent classifier).
        var completion = await _chat.GetResponseAsync(
            new[]
            {
                new ChatMessage(ChatRole.System,
                    "You are ino, an AI-native OS assistant. Politely say you can help with travel planning today."),
                new ChatMessage(ChatRole.User, userText),
            },
            cancellationToken: ct);

        return new SynapseResult(
            Success: true,
            Payload: completion.Message.Text ?? "I can help with travel planning today.",
            Verb: "ino.replied");
    }

    static string ExtractUserText(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return string.Empty;
        try
        {
            var node = JsonNode.Parse(payload);
            return node?["text"]?.GetValue<string>() ?? payload;
        }
        catch
        {
            return payload;
        }
    }

    static bool IsTravelQuery(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var lowered = text.ToLowerInvariant();
        return TravelKeywords.Any(kw => lowered.Contains(kw));
    }
}
```

- [ ] **Step 4: Run tests (both should PASS after Task A10 wires DI)**

Same filter as Step 2. If A10 is not landed yet, mark the two facts `[Fact(Skip = "needs A10 DI registration")]` and unskip in Task A10's commit.

- [ ] **Step 5: Commit**

```bash
git add src/Core/Neurons/Ino/InoNeuronHandler.cs tests/Core.Tests/Neurons/Ino/InoNeuronHandlerTests.cs
git commit -m "feat(ino): InoNeuronHandler as chat root — intent routing + travel delegation"
```

---

### Task A9: Create `InoRootRegistrationStartupTask`

**Files:**
- Create: `src/Core/Neurons/Startup/InoRootRegistrationStartupTask.cs`

- [ ] **Step 1: Implement startup task that registers creator + ino + travel rows**

`src/Core/Neurons/Startup/InoRootRegistrationStartupTask.cs`:
```csharp
using Microsoft.Extensions.Logging;
using Orleans.Runtime;

namespace Core.Neurons.Startup;

// Runs once at silo startup. Ensures the creator + ino + travel:* registry
// rows exist in NeuronRegistryGrain. Idempotent (skip if already present).
public sealed class InoRootRegistrationStartupTask : IStartupTask
{
    readonly IGrainFactory _grains;
    readonly ILogger<InoRootRegistrationStartupTask> _log;

    public InoRootRegistrationStartupTask(IGrainFactory grains, ILogger<InoRootRegistrationStartupTask> log)
    {
        _grains = grains;
        _log = log;
    }

    public async Task Execute(CancellationToken ct)
    {
        var registry = _grains.GetGrain<INeuronRegistry>("global");

        await RegisterIfMissing(registry, new Blueprint(
            Name: "creator",
            Purpose: "Spawns user-facing ino neurons (L3 human-gated in future).",
            Capabilities: new[] { "meta", "lineage" },
            Id: "creator",
            Metadata: new Dictionary<string, string>
            {
                ["lineage"] = "root",
                ["handler"] = "none",
            },
            DomainId: "system"));

        await RegisterIfMissing(registry, new Blueprint(
            Name: "ino",
            Purpose: "User-facing persona. Plans intent, delegates to capability neurons, synthesises responses.",
            Capabilities: new[] { "chat", "planner", "persona" },
            Id: "ino",
            AuthorId: "creator",
            Metadata: new Dictionary<string, string> { ["parent"] = "creator" },
            ToolRefs: new[] { "INeuron" },
            ModelHints: new ModelHints(
                Model: "gpt-5",
                SystemPrompt: "You are ino, an AI-native OS. Plan, delegate, synthesise.",
                Temperature: 0.2f),
            DomainId: "system"));

        await RegisterIfMissing(registry, new Blueprint(
            Name: "travel:recommender",
            Purpose: "Plans a trip by firing synapses at travel:flight-search, travel:hotel-search, travel:place-discovery.",
            Capabilities: new[] { "travel", "planner" },
            Id: "travel:recommender",
            AuthorId: "ino",
            Metadata: new Dictionary<string, string> { ["parent"] = "ino" },
            ToolRefs: new[] { "INeuron" },
            RfwTemplateSource: Ino.Travel.Scripts.DestinationCardRfwScript.Source,
            DomainId: "travel"));

        await RegisterIfMissing(registry, new Blueprint(
            Name: "travel:flight-search",
            Purpose: "Searches flights via SerpApi.",
            Capabilities: new[] { "travel", "flights" },
            Id: "travel:flight-search",
            AuthorId: "travel:recommender",
            Metadata: new Dictionary<string, string> { ["parent"] = "travel:recommender" },
            ToolRefs: new[] { "ISerpApiProviderService" },
            RfwTemplateSource: Ino.Travel.Scripts.FlightCardRfwScript.Source,
            DomainId: "travel"));

        await RegisterIfMissing(registry, new Blueprint(
            Name: "travel:hotel-search",
            Purpose: "Searches hotels via SerpApi.",
            Capabilities: new[] { "travel", "hotels" },
            Id: "travel:hotel-search",
            AuthorId: "travel:recommender",
            ToolRefs: new[] { "ISerpApiProviderService" },
            RfwTemplateSource: Ino.Travel.Scripts.HotelCardRfwScript.Source,
            DomainId: "travel"));

        await RegisterIfMissing(registry, new Blueprint(
            Name: "travel:place-discovery",
            Purpose: "Discovers attractions and food markets via SerpApi.",
            Capabilities: new[] { "travel", "places" },
            Id: "travel:place-discovery",
            AuthorId: "travel:recommender",
            ToolRefs: new[] { "ISerpApiProviderService" },
            RfwTemplateSource: Ino.Travel.Scripts.PlaceCardRfwScript.Source,
            DomainId: "travel"));

        _log.LogInformation("InoRootRegistrationStartupTask: creator + ino + travel:* neurons registered");
    }

    async Task RegisterIfMissing(INeuronRegistry registry, Blueprint blueprint)
    {
        var existing = await registry.TryGetAsync(blueprint.Id!);
        if (existing is not null)
        {
            _log.LogDebug("Registry already contains {Id}; skipping", blueprint.Id);
            return;
        }
        await registry.CreateAsync(blueprint);
    }
}
```

Note: `INeuronRegistry.TryGetAsync` may be named differently in the real interface. Confirm at execution time by reading `src/Core/Neurons/NeuronRegistryGrain.cs` and adjust.

- [ ] **Step 2: Compile**

```
dotnet build src/Core/Core.csproj
```
Expected: build fails on unresolved `Ino.Travel.Scripts.*RfwScript` symbols — those are created in Tasks C1–C4. Order: do Task A11 before landing A9, OR stub the `RfwTemplateSource` fields to `string.Empty` and re-populate in Phase C.

**Decision:** stub them to `null` for Phase A commit; Phase C tasks revisit this file.

Revised Blueprint snippets for A9 commit:
```csharp
RfwTemplateSource: null,
```

- [ ] **Step 3: Commit**

```bash
git add src/Core/Neurons/Startup/InoRootRegistrationStartupTask.cs
git commit -m "feat(ino): startup task registers creator + ino + travel neuron rows"
```

---

### Task A10: DI registration + `InoTestCluster` fixture

**Files:**
- Create: `src/Core/Neurons/Ino/InoServiceCollectionExtensions.cs`
- Create: `tests/Core.Tests/Infrastructure/InoTestCluster.cs`
- Modify: `Aspire/ino.Client/IAWSiloExtensions.cs` — call the new extension

- [ ] **Step 1: Create the DI extension**

`src/Core/Neurons/Ino/InoServiceCollectionExtensions.cs`:
```csharp
using Core.Neurons;
using Core.Neurons.Runtime;
using Core.Neurons.Startup;
using Ino.Travel.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;

namespace Core.Neurons.Ino;

public static class InoServiceCollectionExtensions
{
    public static ISiloBuilder AddInoRuntimeNeurons(this ISiloBuilder silo)
    {
        silo.ConfigureServices(services =>
        {
            services.AddKeyedSingleton<ISynapseHandler, InoNeuronHandler>("ino");
            services.AddKeyedSingleton<ISynapseHandler, TravelRecommenderHandler>("travel:recommender");
            services.AddKeyedSingleton<ISynapseHandler, FlightSearchHandler>("travel:flight-search");
            services.AddKeyedSingleton<ISynapseHandler, HotelSearchHandler>("travel:hotel-search");
            services.AddKeyedSingleton<ISynapseHandler, PlaceDiscoveryHandler>("travel:place-discovery");
        });

        silo.AddStartupTask<InoRootRegistrationStartupTask>();
        return silo;
    }
}
```

- [ ] **Step 2: Wire the extension into the silo registration path**

Read `Aspire/ino.Client/IAWSiloExtensions.cs` and add:
```csharp
siloBuilder.AddInoRuntimeNeurons();
```
after the existing Orleans configuration. Same call inside the test cluster's configurator.

- [ ] **Step 3: Create `InoTestCluster` xUnit fixture**

`tests/Core.Tests/Infrastructure/InoTestCluster.cs`:
```csharp
using IAW.Testing;
using Orleans.TestingHost;
using Xunit;

namespace Core.Tests.Infrastructure;

[CollectionDefinition(nameof(InoTestCluster))]
public sealed class InoTestClusterCollection : ICollectionFixture<InoTestCluster> { }

public sealed class InoTestCluster : IAsyncLifetime
{
    public TestCluster Cluster { get; private set; } = null!;

    public Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<InoSiloConfigurator>();
        Cluster = builder.Build();
        Cluster.Deploy();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        Cluster.StopAllSilos();
        return Task.CompletedTask;
    }

    sealed class InoSiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder.ConfigureServices(InoTestServices.Register);
            siloBuilder.AddInoRuntimeNeurons();
        }
    }
}
```

Helper `InoTestServices.Register` wires the `MockChatClient`, a stub `ISerpApiProviderService` returning deterministic data, and any other test-only services. Create it as a sibling file:

`tests/Core.Tests/Infrastructure/InoTestServices.cs`:
```csharp
using IAW.Testing;
using Ino.Travel.Handlers;
using Ino.Travel.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Core.Tests.Infrastructure;

public static class InoTestServices
{
    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<IChatClient, ToolCallingMockChat>();

        var serp = new Mock<ISerpApiProviderService>();
        serp.Setup(s => s.SearchFlightsAsync(It.IsAny<FlightSearchParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FlightSearchResponse(new[]
            {
                new FlightResult("ANA", "NYC", "NRT", 980m, "USD", "2026-05-12", "13h30m", "morning departure"),
            }));
        serp.Setup(s => s.SearchHotelsAsync(It.IsAny<HotelSearchParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HotelSearchResponse(new[]
            {
                new HotelResult("Andaz Tokyo", "Toranomon", 450m, "USD", 4.7, 0.8, "food markets nearby"),
            }));
        serp.Setup(s => s.SearchPlacesAsync(It.IsAny<PlaceSearchParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaceDiscoveryResponse(new[]
            {
                new PlaceResult("Tsukiji Outer Market", "food_market", "Tsukiji", 4.6,
                    "Historic fish and food market with dozens of street food stalls."),
            }));
        services.AddSingleton(serp.Object);
    }
}
```

- [ ] **Step 4: Unskip and run the tests from A7, A8**

```
dotnet test tests/Core.Tests --filter "FullyQualifiedName~InoNeuronHandlerTests|FullyQualifiedName~TravelRecommenderHandlerTests"
```
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add \
  src/Core/Neurons/Ino/InoServiceCollectionExtensions.cs \
  Aspire/ino.Client/IAWSiloExtensions.cs \
  tests/Core.Tests/Infrastructure/InoTestCluster.cs \
  tests/Core.Tests/Infrastructure/InoTestServices.cs
git commit -m "feat(ino): DI wiring + InoTestCluster fixture for handler tests"
```

---

### Task A11: Chat RPC fires single synapse at `"ino"`

**Files:**
- Modify: `src/Telegram/Program.cs` — replace `RouteTravelAsync` with `RouteInoAsync`

- [ ] **Step 1: Read the current `Chat` method**

```
Read src/Telegram/Program.cs (the whole file)
```

- [ ] **Step 2: Replace `Chat` and delete `RouteTravelAsync` / `IsTravelRelated`**

Replace the method body with:
```csharp
public override async Task<ChatResponse> Chat(ChatRequest request, ServerCallContext context)
{
    var ct = context.CancellationToken;
    var correlationId = Guid.NewGuid().ToString("n");

    var userPayload = JsonSerializer.Serialize(new { text = request.Message });
    var synapse = new Synapse(
        Id: Guid.NewGuid().ToString("n"),
        SourceId: "chat",
        TargetId: "ino",
        Verb: "user.request",
        Payload: userPayload,
        FiredAt: DateTimeOffset.UtcNow,
        CorrelationId: correlationId,
        Decay: 100);

    var ino = clusterClient.GetGrain<INeuron>("ino");
    var result = await ino.HandleAsync(synapse, ct);

    var response = new ChatResponse
    {
        Reply = result.Payload,
        NeuronId = "ino",
        ContentType = "text",
        CorrelationId = correlationId,
    };

    if (result.RfwDescription is not null && result.RfwData is not null)
    {
        response.RfwDescription = ByteString.CopyFrom(result.RfwDescription);
        response.RfwData = ByteString.CopyFrom(result.RfwData);
        response.ContentType = result.Verb;
    }

    return response;
}
```

Delete the `RouteTravelAsync`, `IsTravelRelated`, and `TryBuildRfw` helper methods — their behaviour is now inside the runtime neurons.

- [ ] **Step 3: If `ChatResponse` proto has no `CorrelationId` field, add it**

Read `src/Telegram/protos/ino.proto`. If missing, add:
```proto
message ChatResponse {
  string reply = 1;
  string neuron_id = 2;
  bytes rfw_description = 3;
  bytes rfw_data = 4;
  string content_type = 5;
  string correlation_id = 6;
}
```
Regenerate gRPC stubs via `dotnet build src/Telegram/Telegram.csproj`.

- [ ] **Step 4: Build + run full test suite**

```
dotnet build ino.slnx
dotnet test tests/Core.Tests
```
Expected: build green; existing travel tests may fail because `NeuronId` assertions compare against `"TravelRecommender"`. Task A12 fixes those.

- [ ] **Step 5: Commit**

```bash
git add src/Telegram/Program.cs src/Telegram/protos/ino.proto
git commit -m "feat(telegram): Chat RPC routes every message to ino neuron"
```

---

### Task A12: Update existing travel E2E tests

**Files:**
- Modify: all tests under `tests/E2E.Tests/Travel/` that assert `response.NeuronId`

- [ ] **Step 1: For each of the 6 travel E2E files, change**

```csharp
Assert.Equal("TravelRecommender", response.NeuronId);
```
to
```csharp
Assert.Equal("ino", response.NeuronId);
```

- [ ] **Step 2: Add a timeline assertion helper in `NeuronE2ETest` and use it**

Add method:
```csharp
protected async Task AssertSynapseChainFiredAsync(string correlationId, params string[] targetNeuronIds)
{
    var reader = Host.Cluster.Client.GetGrain<ITimelineReader>("global");
    var events = await reader.QueryByCorrelationAsync(correlationId);
    foreach (var target in targetNeuronIds)
        Assert.Contains(events, e => e.TargetId == target && e.Kind == TimelineEventKind.SynapseFired);
}
```

(Phase D may add `QueryByCorrelationAsync` if it doesn't exist; if so, defer this assertion helper until Phase D and just update the NeuronId assertion in Phase A.)

- [ ] **Step 3: Run all travel E2Es**

```
dotnet test tests/E2E.Tests --filter "FullyQualifiedName~Travel"
```
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add tests/E2E.Tests/Travel/*.cs tests/E2E.Tests/Infrastructure/NeuronE2ETest.cs
git commit -m "test(travel): update existing E2Es to assert NeuronId=ino after runtime migration"
```

---

### Task A13: End-of-phase Aspire verification

- [ ] **Step 1: Build the solution**
```
dotnet build ino.slnx
```

- [ ] **Step 2: Start Aspire and confirm every resource Healthy**
```
aspire start
```
Then check `https://localhost:17280` — all resources green.

- [ ] **Step 3: Drive an ino chat via MCP**
```
ToolSearch(query="select:mcp__iaw__assistant_chat,mcp__iaw__agent_get_events", max_results=2)
mcp__iaw__assistant_chat(message="Plan 5 days in Tokyo mid-May food markets")
```
Expected: text response mentions Tokyo and comes through the `"ino"` routing path.

- [ ] **Step 4: Cross-check Aspire traces**
Open dashboard Traces tab, confirm the turn's trace tree shows `ino → travel:recommender → {flight-search, hotel-search, place-discovery}` spans.

- [ ] **Step 5: Run full test suite one more time**
```
dotnet test ino.slnx
```
Expected: all green.

No commit — this is verification only.

---

## Phase C — `travel:itinerary-composer` L1

### Task C1: Create `FlightCardRfwScript.cs`, `HotelCardRfwScript.cs`, `PlaceCardRfwScript.cs`

**Files:**
- Create: `domains/travel/Ino.Travel/Scripts/FlightCardRfwScript.cs`
- Create: `domains/travel/Ino.Travel/Scripts/HotelCardRfwScript.cs`
- Create: `domains/travel/Ino.Travel/Scripts/PlaceCardRfwScript.cs`
- Create: `domains/travel/Ino.Travel/Scripts/DestinationCardRfwScript.cs`

- [ ] **Step 1: Write each `*RfwScript` file as a static `Source` string constant**

Each file follows the same shape. Example for flights:

`domains/travel/Ino.Travel/Scripts/FlightCardRfwScript.cs`:
```csharp
namespace Ino.Travel.Scripts;

public static class FlightCardRfwScript
{
    // Roslyn script body executed by NeuronGrain's RfwTemplateSource post-pass.
    // Globals: Result (SynapseResult) + Rfw (RfwBuilder).
    public const string Source = """
        using System.Text.Json;
        using System.Linq;

        var doc = JsonDocument.Parse(Result.Payload);
        var items = doc.RootElement.GetProperty("Items").EnumerateArray().ToList();

        var children = string.Join(",\n", items.Select((el, i) =>
            $$"""
            FlightCard(
                airline: data.items.{{i}}.airline,
                from: data.items.{{i}}.from,
                to: data.items.{{i}}.to,
                price: data.items.{{i}}.price,
                date: data.items.{{i}}.date,
                duration: data.items.{{i}}.duration
            )
            """));

        Rfw.Description = $$"""
            import ino.flights;
            widget root = Column(children: [
                {{children}}
            ]);
            """;

        var itemData = items.Select(el => new
        {
            airline = el.GetProperty("Airline").GetString(),
            from = el.GetProperty("From").GetString(),
            to = el.GetProperty("To").GetString(),
            price = el.GetProperty("Price").GetDecimal(),
            date = el.GetProperty("Date").GetString(),
            duration = el.GetProperty("Duration").GetString(),
        }).ToList();

        Rfw.Data["items"] = itemData;
        """;
}
```

Mirror the pattern for `HotelCardRfwScript`, `PlaceCardRfwScript`, and `DestinationCardRfwScript`. Each reads its payload's `Items` array and binds the relevant fields per the DSL already used in `FlightCardTemplate.cs` before deletion.

- [ ] **Step 2: Compile**
```
dotnet build domains/travel/Ino.Travel/Ino.Travel.csproj
```

- [ ] **Step 3: Delete the old static template classes**
```
rm domains/travel/Ino.Travel/UI/FlightCardTemplate.cs
rm domains/travel/Ino.Travel/UI/HotelCardTemplate.cs
rm domains/travel/Ino.Travel/UI/PlaceCardTemplate.cs
rm domains/travel/Ino.Travel/UI/DestinationCardTemplate.cs
```

- [ ] **Step 4: Grep for references and fix**
```
Grep(pattern="FlightCardTemplate|HotelCardTemplate|PlaceCardTemplate|DestinationCardTemplate", path="/e/ino")
```
Expected: only references are `TryBuildRfw` helpers already removed in A11. If anything else appears, update it to use the new `*RfwScript` classes.

- [ ] **Step 5: Re-point `InoRootRegistrationStartupTask` RfwTemplateSource fields** to the new `*RfwScript.Source` constants (no longer `null`).

- [ ] **Step 6: Build + run tests, commit**
```
dotnet build ino.slnx
dotnet test tests/E2E.Tests --filter "FullyQualifiedName~FlightSearchE2E|FullyQualifiedName~HotelSearchE2E|FullyQualifiedName~PlaceDiscoveryE2E"
```
Expected: PASS (each E2E now asserts a card rendered via `RfwTemplateSource` post-pass).

```bash
git add domains/travel/Ino.Travel/Scripts/ \
        src/Core/Neurons/Startup/InoRootRegistrationStartupTask.cs
git rm domains/travel/Ino.Travel/UI/FlightCardTemplate.cs \
       domains/travel/Ino.Travel/UI/HotelCardTemplate.cs \
       domains/travel/Ino.Travel/UI/PlaceCardTemplate.cs \
       domains/travel/Ino.Travel/UI/DestinationCardTemplate.cs
git commit -m "refactor(travel): card templates -> RfwTemplateSource scripts on neuron rows"
```

---

### Task C2: `ItineraryComposerScript.Source` (L1 script body)

**Files:**
- Create: `domains/travel/Ino.Travel/Scripts/ItineraryComposerScript.cs`

- [ ] **Step 1: Write the script body**

`domains/travel/Ino.Travel/Scripts/ItineraryComposerScript.cs`:
```csharp
namespace Ino.Travel.Scripts;

// Pure L1: this string is stored in the "travel:itinerary-composer" Neuron row's
// ScriptSource. NeuronGrain's runtime compiles it via CSharpScript on first activation,
// caches the delegate by SHA256, and re-runs on every synapse. No compiled class.
//
// Globals available: NeuronScriptGlobals { Grains, NeuronId, Synapse, Log, Tools, Chat, Rfw }
public static class ItineraryComposerScript
{
    public const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Text.Json;
        using System.Threading.Tasks;
        using Core.Contracts;
        using Core.Neurons;
        using Core.Neurons.Ino;

        // 1. Decode envelope and inner ItineraryRequest payload.
        PersonaContext.TryRead(Synapse.Payload, out var persona, out var innerRequest);
        using var reqDoc = JsonDocument.Parse(innerRequest);
        var destination = reqDoc.RootElement.GetProperty("Destination").GetString() ?? "";
        var startDate = reqDoc.RootElement.GetProperty("StartDate").GetString() ?? "";
        var dayCount = reqDoc.RootElement.GetProperty("DayCount").GetInt32();

        // 2. Fire three sub-synapses in parallel.
        async Task<string> FireAsync(string target, string verb, object payload)
        {
            var envelope = PersonaContext.WrapRequest(persona, JsonSerializer.Serialize(payload));
            var subSynapse = new Synapse(
                Id: Guid.NewGuid().ToString("n"),
                SourceId: NeuronId,
                TargetId: target,
                Verb: verb,
                Payload: envelope,
                FiredAt: DateTimeOffset.UtcNow,
                CorrelationId: Synapse.CorrelationId,
                Decay: 100);
            var child = Grains.GetGrain<INeuron>(target);
            var result = await child.HandleAsync(subSynapse);
            return result.Payload;
        }

        var flightTask = FireAsync("travel:flight-search", "search_flights",
            new { From = "NYC", To = destination, DepartureDate = startDate, ReturnDate = (string?)null, CabinClass = "economy", PassengerCount = 1 });
        var hotelTask = FireAsync("travel:hotel-search", "search_hotels",
            new { Location = destination, CheckIn = startDate, CheckOut = startDate, Guests = 1, PriceTier = "mid_range" });
        var placeTask = FireAsync("travel:place-discovery", "search_places",
            new { Location = destination, Category = (string?)null });

        await Task.WhenAll(flightTask, hotelTask, placeTask);

        // 3. Compose a typed ItineraryView.
        var days = new List<object>();
        for (int i = 0; i < dayCount; i++)
        {
            days.Add(new
            {
                DayNumber = i + 1,
                Date = DateTime.Parse(startDate).AddDays(i).ToString("yyyy-MM-dd"),
                Headline = i == 0 ? $"Arrive in {destination}" : $"Day {i + 1} in {destination}",
                Entries = new[]
                {
                    new { Time = "09:00", Title = "Morning", Detail = "Explore a nearby food market.", Kind = "place" },
                    new { Time = "13:00", Title = "Lunch", Detail = "Neighborhood pick.",               Kind = "meal" },
                    new { Time = "15:00", Title = "Afternoon", Detail = "Cultural highlight.",          Kind = "place" },
                    new { Time = "19:00", Title = "Dinner", Detail = "Reservation.",                     Kind = "meal" },
                }
            });
        }

        var view = new
        {
            Destination = destination,
            Summary = $"A {dayCount}-day itinerary in {destination}.",
            Days = days,
        };

        return new SynapseResult(
            Success: true,
            Payload: JsonSerializer.Serialize(view),
            Verb: "itinerary.composed");
        """;
}
```

Note: the `FireAsync` loop above is illustrative — the exact per-day composition logic may blend persona interests into place selection once place results are parsed. For the demo, the scaffolded structure is sufficient; an iteration task in Phase G hardens it.

- [ ] **Step 2: Compile**
```
dotnet build domains/travel/Ino.Travel/Ino.Travel.csproj
```

- [ ] **Step 3: Commit**
```bash
git add domains/travel/Ino.Travel/Scripts/ItineraryComposerScript.cs
git commit -m "feat(travel): ItineraryComposerScript L1 source — fires child synapses and composes ItineraryView"
```

---

### Task C3: `ItineraryCardRfwScript.Source` (RFW template for the itinerary card)

**Files:**
- Create: `domains/travel/Ino.Travel/Scripts/ItineraryCardRfwScript.cs`

- [ ] **Step 1: Write the RFW script**

`domains/travel/Ino.Travel/Scripts/ItineraryCardRfwScript.cs`:
```csharp
namespace Ino.Travel.Scripts;

public static class ItineraryCardRfwScript
{
    public const string Source = """
        using System.Text.Json;
        using System.Linq;

        var doc = JsonDocument.Parse(Result.Payload);
        var days = doc.RootElement.GetProperty("Days").EnumerateArray().ToList();

        var dayChildren = string.Join(",\n", days.Select((d, i) =>
            $$"""
            ItineraryDay(
                dayNumber: data.days.{{i}}.dayNumber,
                date: data.days.{{i}}.date,
                headline: data.days.{{i}}.headline,
                entries: data.days.{{i}}.entries
            )
            """));

        Rfw.Description = $$"""
            import ino.itinerary;
            widget root = ItineraryCard(
                destination: data.destination,
                summary: data.summary,
                days: [
                    {{dayChildren}}
                ]
            );
            """;

        Rfw.Data["destination"] = doc.RootElement.GetProperty("Destination").GetString() ?? "";
        Rfw.Data["summary"] = doc.RootElement.GetProperty("Summary").GetString() ?? "";
        Rfw.Data["days"] = days.Select(d => new
        {
            dayNumber = d.GetProperty("DayNumber").GetInt32(),
            date = d.GetProperty("Date").GetString(),
            headline = d.GetProperty("Headline").GetString(),
            entries = d.GetProperty("Entries").EnumerateArray().Select(e => new
            {
                time = e.GetProperty("Time").GetString(),
                title = e.GetProperty("Title").GetString(),
                detail = e.GetProperty("Detail").GetString(),
                kind = e.GetProperty("Kind").GetString(),
            }).ToList()
        }).ToList();
        """;
}
```

- [ ] **Step 2: Commit**
```bash
git add domains/travel/Ino.Travel/Scripts/ItineraryCardRfwScript.cs
git commit -m "feat(travel): ItineraryCardRfwScript — RFW template for the itinerary card"
```

---

### Task C4: Register `travel:itinerary-composer` Neuron row

**Files:**
- Modify: `src/Core/Neurons/Startup/InoRootRegistrationStartupTask.cs`
- Modify: `src/Core/Neurons/Ino/InoNeuronHandler.cs` — extend intent routing to fire at itinerary-composer when the request is a full trip plan

- [ ] **Step 1: Add the itinerary composer Blueprint**

In `InoRootRegistrationStartupTask.Execute`, after the existing `travel:place-discovery` row, append:
```csharp
await RegisterIfMissing(registry, new Blueprint(
    Name: "travel:itinerary-composer",
    Purpose: "Composes a multi-day itinerary by firing child synapses at flight/hotel/place neurons.",
    Capabilities: new[] { "travel", "planner", "composer" },
    Id: "travel:itinerary-composer",
    AuthorId: "ino",
    Metadata: new Dictionary<string, string>
    {
        ["parent"] = "ino",
        ["layer"] = "L1",
    },
    ScriptSource: Ino.Travel.Scripts.ItineraryComposerScript.Source,
    RfwTemplateSource: Ino.Travel.Scripts.ItineraryCardRfwScript.Source,
    ToolRefs: new[] { "INeuron" },
    DomainId: "travel"));
```

Note: no `ISynapseHandler` is registered for `"travel:itinerary-composer"` — dispatch falls through to the `ScriptSource` path inside `NeuronGrain.HandleAsync`.

- [ ] **Step 2: Extend `InoNeuronHandler` routing**

Add a "trip plan" detector that routes to the composer instead of the recommender when the user request expresses a full itinerary intent:
```csharp
static bool WantsItinerary(string text) =>
    text.Contains("itinerary", StringComparison.OrdinalIgnoreCase) ||
    (text.Contains("plan", StringComparison.OrdinalIgnoreCase) &&
     text.Contains("day", StringComparison.OrdinalIgnoreCase));
```

In `HandleAsync`, before the travel:recommender branch:
```csharp
if (WantsItinerary(userText))
{
    var tripRequest = new
    {
        Destination = ExtractDestination(userText),
        StartDate = "2026-05-12", // phase G will parse from text
        DayCount = ExtractDayCount(userText),
    };
    // ... wrap in PersonaContext + fire at "travel:itinerary-composer"
}
```

Add `ExtractDestination` (simple regex over proper nouns) + `ExtractDayCount` (`"5 days"` → 5 via regex). Both are Phase-G-hardened later; for demo they default to `"Tokyo"` / `5`.

- [ ] **Step 3: Write an E2E test for the itinerary path**

`tests/E2E.Tests/Travel/ItineraryComposerE2E.cs`:
```csharp
using IAW.E2E.Tests.Infrastructure;
using Xunit;

namespace IAW.E2E.Tests.Travel;

public class ItineraryComposerE2E(GrpcTestFixture fixture) : NeuronE2ETest(fixture)
{
    [Fact(Timeout = 120_000)]
    [Trait("Category", "E2E")]
    public async Task PlanFullTokyoTrip_RendersItineraryCard()
    {
        MockLlm.Reset();
        MockLlm
            .OnMultiToolCall(
                ("fire_flight_search",  [("from","NYC"),("to","Tokyo"),("departureDate","2026-05-12"),("returnDate",(object)"2026-05-17")]),
                ("fire_hotel_search",   [("location","Tokyo"),("checkIn","2026-05-12"),("checkOut","2026-05-17")]),
                ("fire_place_discovery",[("location","Tokyo"),("category",(object)"food_market")]))
            .WithFinalResponse("Here is your 5-day Tokyo itinerary.");

        var response = await ChatAsync("Plan me a 5-day itinerary for Tokyo starting 2026-05-12 with food markets");

        Assert.True(response.RfwDescription.Length > 0,
            "Expected itinerary RFW bytes to be attached to the response");
        var dsl = System.Text.Encoding.UTF8.GetString(response.RfwDescription.ToByteArray());
        Assert.Contains("ItineraryCard", dsl);
        Assert.Contains("import ino.itinerary", dsl);
    }
}
```

- [ ] **Step 4: Run test**
```
dotnet test tests/E2E.Tests --filter "FullyQualifiedName~ItineraryComposerE2E"
```
Expected: PASS.

- [ ] **Step 5: Commit**
```bash
git add src/Core/Neurons/Startup/InoRootRegistrationStartupTask.cs \
        src/Core/Neurons/Ino/InoNeuronHandler.cs \
        tests/E2E.Tests/Travel/ItineraryComposerE2E.cs
git commit -m "feat(travel): register travel:itinerary-composer L1 and wire ino routing"
```

---

## Phase E — Flutter chat thread + Rive persona orb

### Task E1: Ship `assets/rive/persona_orb.riv`

**Files:**
- Create: `clients/ino.flutter/assets/rive/persona_orb.riv`

- [ ] **Step 1: Source or ship a placeholder `.riv` file**

The demo falls back to CustomPaint if the file is absent. Two acceptable outcomes:
1. Use any existing licensed Rive file you own with the `Persona` state machine matching the contract below. Drop it at the path above.
2. Commit an empty file (`touch clients/ino.flutter/assets/rive/persona_orb.riv`) which the Rive runtime rejects → the fallback renders.

If option 2 is taken, add a README in `clients/ino.flutter/assets/rive/README.md` describing the contract:
```
State machine name: Persona
Number inputs:
  - mood     float [0..1] (0=sleeping, 0.2=idle, 0.4=listening, 0.6=thinking, 0.8=acting, 1.0=celebrating)
  - energy   float [0..1]
  - pulse    float [0..1] (momentary — reset by controller)
Triggers:
  - trigger_searching_flights
  - trigger_searching_hotels
  - trigger_searching_places
  - trigger_composing_itinerary
  - trigger_thinking
  - trigger_idle
Artboard name: PersonaOrb (any 1:1 aspect, recommended 400x400)
```

- [ ] **Step 2: Commit**
```bash
git add clients/ino.flutter/assets/rive/persona_orb.riv clients/ino.flutter/assets/rive/README.md
git commit -m "chore(flutter): persona_orb.riv placeholder + contract documentation"
```

---

### Task E2: Create `RivePersonaOrb` widget with CustomPaint fallback

**Files:**
- Create: `clients/ino.flutter/lib/persona/rive_persona_orb.dart`

- [ ] **Step 1: Write the widget**

`clients/ino.flutter/lib/persona/rive_persona_orb.dart`:
```dart
import 'package:flutter/material.dart';
import 'package:rive/rive.dart';
import 'package:ino_flutter/persona/persona_state.dart';

typedef PersonaFallbackBuilder = Widget Function(BuildContext, PersonaStateModel);

class RivePersonaOrb extends StatefulWidget {
  const RivePersonaOrb({
    super.key,
    required this.persona,
    required this.size,
    required this.fallbackBuilder,
    this.assetPath = 'assets/rive/persona_orb.riv',
    this.stateMachineName = 'Persona',
  });

  final PersonaStateModel persona;
  final double size;
  final PersonaFallbackBuilder fallbackBuilder;
  final String assetPath;
  final String stateMachineName;

  @override
  State<RivePersonaOrb> createState() => _RivePersonaOrbState();
}

class _RivePersonaOrbState extends State<RivePersonaOrb> {
  StateMachineController? _controller;
  SMIInput<double>? _moodInput;
  SMIInput<double>? _energyInput;
  SMIInput<double>? _pulseInput;
  bool _loadFailed = false;

  void _onRiveInit(Artboard artboard) {
    final controller =
        StateMachineController.fromArtboard(artboard, widget.stateMachineName);
    if (controller == null) {
      setState(() => _loadFailed = true);
      return;
    }
    artboard.addController(controller);
    _controller = controller;
    _moodInput = controller.findInput<double>('mood');
    _energyInput = controller.findInput<double>('energy');
    _pulseInput = controller.findInput<double>('pulse');
    _applyPersona();
  }

  void _applyPersona() {
    if (_controller == null) return;
    _moodInput?.value = _moodFor(widget.persona.emotion);
    _energyInput?.value = widget.persona.energy;
    _pulseInput?.value = widget.persona.signalPulse;
  }

  static double _moodFor(PersonaEmotion emotion) => switch (emotion) {
        PersonaEmotion.sleeping => 0.0,
        PersonaEmotion.waking => 0.1,
        PersonaEmotion.idle => 0.2,
        PersonaEmotion.listening => 0.4,
        PersonaEmotion.thinking => 0.6,
        PersonaEmotion.searching => 0.7,
        PersonaEmotion.acting => 0.8,
        PersonaEmotion.presenting => 0.85,
        PersonaEmotion.responding => 0.9,
        PersonaEmotion.celebrating => 1.0,
        PersonaEmotion.confused => 0.35,
        PersonaEmotion.evolving => 0.75,
      };

  @override
  void didUpdateWidget(covariant RivePersonaOrb old) {
    super.didUpdateWidget(old);
    _applyPersona();
  }

  @override
  void dispose() {
    _controller?.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    if (_loadFailed) {
      return widget.fallbackBuilder(context, widget.persona);
    }
    return SizedBox(
      width: widget.size,
      height: widget.size,
      child: RiveAnimation.asset(
        widget.assetPath,
        stateMachines: [widget.stateMachineName],
        onInit: _onRiveInit,
        placeHolder: widget.fallbackBuilder(context, widget.persona),
      ),
    );
  }
}
```

Note: verify `SMIInput<double>` vs. `SMINumber` API in Context7 step A0-4. Adjust if 0.14.5 uses the typed concrete class instead of the generic.

- [ ] **Step 2: Compile via flutter analyze**
```
cd clients/ino.flutter && flutter analyze lib/persona/rive_persona_orb.dart
```
Expected: no errors.

- [ ] **Step 3: Commit**
```bash
git add clients/ino.flutter/lib/persona/rive_persona_orb.dart
git commit -m "feat(flutter): RivePersonaOrb with CustomPaint fallback on load failure"
```

---

### Task E3: Replace `_RivePlaceholder` in `persona_widget.dart`

**Files:**
- Modify: `clients/ino.flutter/lib/persona/persona_widget.dart`

- [ ] **Step 1: Swap the widget**

In `build()`, replace:
```dart
if (persona.riveAssetUrl != null)
  _RivePlaceholder(assetUrl: persona.riveAssetUrl!, size: widget.size)
else
  AnimatedBuilder(...)
```

with:
```dart
RivePersonaOrb(
  persona: persona,
  size: widget.size,
  fallbackBuilder: (context, personaModel) => AnimatedBuilder(
    animation: Listenable.merge([_renderLoop, _pulseDecay]),
    builder: (context, _) => CustomPaint(
      size: Size(widget.size, widget.size),
      painter: _PersonaPainter(
        emotion: personaModel.emotion,
        energy: personaModel.energy,
        neuronCount: personaModel.neuronCount,
        synapseRate: personaModel.synapseRate,
        animationValue: _renderLoop.value,
        signalPulse: _currentPulse,
        activeSkillCount: personaModel.activeSkillCount,
      ),
    ),
  ),
)
```

Delete the `_RivePlaceholder` class — it's no longer referenced.

- [ ] **Step 2: `flutter analyze`** — expect no warnings.

- [ ] **Step 3: Build Flutter web + copy to Telegram wwwroot**
```
cd clients/ino.flutter && flutter build web --no-tree-shake-icons
cp -r build/web/* ../../src/Telegram/wwwroot/
```

- [ ] **Step 4: Restart telegram resource**
```
mcp__aspire__execute_resource_command(resourceName="telegram", commandName="rebuild")
```

- [ ] **Step 5: Manual verify**
Open `http://localhost:65437` in a browser, confirm the persona orb renders (Rive or CustomPaint fallback). Inspect browser console — should be 200 OK.

- [ ] **Step 6: Commit**
```bash
git add clients/ino.flutter/lib/persona/persona_widget.dart
git commit -m "feat(flutter): swap _RivePlaceholder for RivePersonaOrb with fallback"
```

---

### Task E4: `ChatMessageTile` — per-message widget

**Files:**
- Create: `clients/ino.flutter/lib/screens/home/chat_message_tile.dart`

- [ ] **Step 1: Write the widget**

```dart
import 'package:flutter/material.dart';
import 'package:ino_flutter/state/ino_bloc.dart';
import 'package:rfw/rfw.dart';

class ChatMessageTile extends StatelessWidget {
  const ChatMessageTile({super.key, required this.message});

  final ChatMessage message;

  @override
  Widget build(BuildContext context) {
    if (message.isUser) {
      return _UserBubble(text: message.text);
    }
    if (message.hasRfw) {
      return _AssistantRfwCard(message: message);
    }
    return _AssistantText(text: message.text);
  }
}

class _UserBubble extends StatelessWidget {
  const _UserBubble({required this.text});
  final String text;

  @override
  Widget build(BuildContext context) {
    return Align(
      alignment: Alignment.centerRight,
      child: Container(
        margin: const EdgeInsets.symmetric(vertical: 6, horizontal: 12),
        padding: const EdgeInsets.symmetric(vertical: 10, horizontal: 14),
        decoration: BoxDecoration(
          color: Colors.indigo.shade400,
          borderRadius: BorderRadius.circular(18),
        ),
        child: Text(text, style: const TextStyle(color: Colors.white)),
      ),
    );
  }
}

class _AssistantText extends StatelessWidget {
  const _AssistantText({required this.text});
  final String text;

  @override
  Widget build(BuildContext context) {
    return Align(
      alignment: Alignment.centerLeft,
      child: Container(
        margin: const EdgeInsets.symmetric(vertical: 6, horizontal: 12),
        padding: const EdgeInsets.symmetric(vertical: 10, horizontal: 14),
        decoration: BoxDecoration(
          color: Colors.grey.shade200,
          borderRadius: BorderRadius.circular(18),
        ),
        child: Text(text),
      ),
    );
  }
}

class _AssistantRfwCard extends StatefulWidget {
  const _AssistantRfwCard({required this.message});
  final ChatMessage message;

  @override
  State<_AssistantRfwCard> createState() => _AssistantRfwCardState();
}

class _AssistantRfwCardState extends State<_AssistantRfwCard> {
  final _runtime = Runtime();
  final _data = DynamicContent();

  @override
  void initState() {
    super.initState();
    final libraryName = LibraryName(['ino', 'flights']);
    _runtime.update(libraryName, parseLibraryFile(widget.message.rfwDescription!));
    _data.update('data', widget.message.rfwData as Map<String, Object?>);
  }

  @override
  Widget build(BuildContext context) {
    return Align(
      alignment: Alignment.centerLeft,
      child: Container(
        margin: const EdgeInsets.symmetric(vertical: 6, horizontal: 12),
        constraints: const BoxConstraints(maxWidth: 480),
        child: RemoteWidget(
          runtime: _runtime,
          data: _data,
          widget: const FullyQualifiedWidgetName(
            LibraryName(['main']),
            'root',
          ),
          onEvent: (name, arguments) {},
        ),
      ),
    );
  }
}
```

- [ ] **Step 2: `flutter analyze`** — confirm clean.

- [ ] **Step 3: Commit**
```bash
git add clients/ino.flutter/lib/screens/home/chat_message_tile.dart
git commit -m "feat(flutter): ChatMessageTile — text / RFW / user bubble widget"
```

---

### Task E5: Refactor `home_screen.dart` to append-only `ListView` with entrance animation

**Files:**
- Modify: `clients/ino.flutter/lib/screens/home/home_screen.dart`

- [ ] **Step 1: Read the current home_screen**

```
Read clients/ino.flutter/lib/screens/home/home_screen.dart
```

- [ ] **Step 2: Replace the message section with**

```dart
Expanded(
  child: BlocBuilder<InoBloc, InoBlocState>(
    builder: (context, state) {
      return ListView.builder(
        reverse: false,
        padding: const EdgeInsets.symmetric(vertical: 12),
        itemCount: state.messages.length,
        itemBuilder: (context, index) {
          final message = state.messages[index];
          return TweenAnimationBuilder<double>(
            key: ValueKey('msg-$index'),
            tween: Tween(begin: 0.0, end: 1.0),
            duration: const Duration(milliseconds: 280),
            curve: Curves.easeOutCubic,
            builder: (context, t, child) => Opacity(
              opacity: t,
              child: Transform.translate(
                offset: Offset(0, (1 - t) * 16),
                child: child,
              ),
            ),
            child: ChatMessageTile(message: message),
          );
        },
      );
    },
  ),
),
```

Ensure `InoBloc._onMessageReceived` uses `state.copyWith(messages: [...state.messages, newMessage])`. If any code path replaces (`state.messages.last = ...`), refactor it to append.

- [ ] **Step 3: Build + copy + rebuild telegram**

```
cd clients/ino.flutter && flutter build web --no-tree-shake-icons
cp -r build/web/* ../../src/Telegram/wwwroot/
mcp__aspire__execute_resource_command(resourceName="telegram", commandName="rebuild")
```

- [ ] **Step 4: Manual verify**
Open browser, send two messages, confirm both are visible in order with slide-up animation.

- [ ] **Step 5: Commit**
```bash
git add clients/ino.flutter/lib/screens/home/home_screen.dart
git commit -m "feat(flutter): home screen append-only ListView with entrance animation"
```

---

## Phase F — `TokyoTripPlanningE2E` Playwright test + observability

### Task F1: Create the full-flow Playwright E2E

**Files:**
- Create: `tests/E2E.Tests/Travel/TokyoTripPlanningE2E.cs`

- [ ] **Step 1: Write the test**

```csharp
using IAW.E2E.Tests.Infrastructure;
using Xunit;

namespace IAW.E2E.Tests.Travel;

public class TokyoTripPlanningE2E(GrpcTestFixture fixture) : NeuronE2ETest(fixture)
{
    [Fact(Timeout = 180_000)]
    [Trait("Category", "E2E")]
    public async Task FullTokyoFlow_RendersFlightsHotelsItinerary()
    {
        MockLlm.Reset();

        // Turn 1: user asks for a destination overview
        MockLlm.WithFinalResponse("Tokyo highlights: Shibuya, Asakusa, Tsukiji.");
        var destinationResponse = await ChatAsync("Plan me 5 days in Tokyo, mid-May, mid-range budget, I like food markets.");
        Assert.Equal("ino", destinationResponse.NeuronId);

        // Turn 2: user asks for flights
        MockLlm.OnToolCalled("fire_flight_search", args =>
            "{\"Items\":[{\"Airline\":\"ANA\",\"From\":\"NYC\",\"To\":\"NRT\",\"Price\":980,\"Currency\":\"USD\",\"Date\":\"2026-05-12\",\"Duration\":\"13h30m\",\"BestForYouReason\":\"morning departure\"}]}");
        MockLlm.WithFinalResponse("Flights found.");
        var flightResponse = await ChatAsync("Show me flights to Tokyo on 2026-05-12");
        Assert.True(flightResponse.RfwDescription.Length > 0);
        var flightDsl = System.Text.Encoding.UTF8.GetString(flightResponse.RfwDescription.ToByteArray());
        Assert.Contains("FlightCard", flightDsl);

        // Turn 3: user asks for hotels
        MockLlm.OnToolCalled("fire_hotel_search", args =>
            "{\"Items\":[{\"Name\":\"Andaz Tokyo\",\"Location\":\"Toranomon\",\"PricePerNight\":450,\"Currency\":\"USD\",\"Rating\":4.7,\"WalkingDistanceToLandmark\":0.8,\"BestForYouReason\":\"food markets nearby\"}]}");
        MockLlm.WithFinalResponse("Hotels found.");
        var hotelResponse = await ChatAsync("Now find me hotels near a food market");
        Assert.True(hotelResponse.RfwDescription.Length > 0);
        Assert.Contains("HotelCard",
            System.Text.Encoding.UTF8.GetString(hotelResponse.RfwDescription.ToByteArray()));

        // Turn 4: user asks for the full itinerary — triggers travel:itinerary-composer L1
        MockLlm.OnMultiToolCall(
            ("fire_flight_search",  [("from","NYC"),("to","Tokyo"),("departureDate","2026-05-12"),("returnDate",(object)"2026-05-17")]),
            ("fire_hotel_search",   [("location","Tokyo"),("checkIn","2026-05-12"),("checkOut","2026-05-17")]),
            ("fire_place_discovery",[("location","Tokyo"),("category",(object)"food_market")]))
            .WithFinalResponse("Your 5-day Tokyo itinerary is ready.");
        var itineraryResponse = await ChatAsync("Now build me the 5-day itinerary");
        Assert.True(itineraryResponse.RfwDescription.Length > 0);
        var itineraryDsl = System.Text.Encoding.UTF8.GetString(
            itineraryResponse.RfwDescription.ToByteArray());
        Assert.Contains("ItineraryCard", itineraryDsl);

        // Playwright: browser render of the final itinerary + screenshots
        var (page, grpcBody) = await OpenBrowserAndVerify("Now build me the 5-day itinerary");
        AssertGrpcResponseContains(grpcBody, "ItineraryCard");
        await TakeScreenshot(page, "tokyo-trip-planning-itinerary");

        // Extra screenshot: scroll the thread to show all 4 cards stacked
        await page.EvaluateAsync("() => window.scrollTo(0, document.body.scrollHeight)");
        await TakeScreenshot(page, "tokyo-trip-planning-thread");
    }
}
```

- [ ] **Step 2: Run headless**
```
INO_E2E_NO_BROWSER=false dotnet test tests/E2E.Tests --filter "FullyQualifiedName~TokyoTripPlanningE2E"
```
Expected: PASS. Screenshots appear at `tests/E2E.Tests/bin/Debug/net11.0/win-x64/screenshots/tokyo-trip-planning-itinerary.png` and `tokyo-trip-planning-thread.png`.

- [ ] **Step 3: Commit**
```bash
git add tests/E2E.Tests/Travel/TokyoTripPlanningE2E.cs
git commit -m "test(e2e): TokyoTripPlanningE2E — full multi-turn trip planning Playwright flow"
```

---

### Task F2: Add travel-specific OTel spans

**Files:**
- Modify: `src/Core/Neurons/Ino/InoNeuronHandler.cs` — add `using var activity = IaVActivitySource.Start("ino.handle")`
- Modify: each travel handler — add `ino.{flight,hotel,place}.search` spans
- Modify: ItineraryComposerScript — emit telemetry via `Log.LogInformation` with structured properties that show up in Aspire dashboard

- [ ] **Step 1: Add a shared ActivitySource**

`src/Core/Neurons/Ino/InoTelemetry.cs`:
```csharp
using System.Diagnostics;

namespace Core.Neurons.Ino;

public static class InoTelemetry
{
    public static readonly ActivitySource Source = new("ino");
}
```

- [ ] **Step 2: Wrap each handler `HandleAsync` body**

```csharp
using var span = InoTelemetry.Source.StartActivity("ino.flight.search");
span?.SetTag("synapse.correlation_id", synapse.CorrelationId);
span?.SetTag("synapse.verb", synapse.Verb);
```

- [ ] **Step 3: Register `InoTelemetry.Source` with OTel tracing provider**

In `Aspire/ino.Client/OpenTelemetryExtensions.cs`, add `.AddSource("ino")` to the tracing builder.

- [ ] **Step 4: Run Aspire, drive a chat, verify traces in dashboard**
```
aspire start
mcp__iaw__assistant_chat(message="Plan 5 days in Tokyo mid-May food markets")
```
Open Aspire dashboard → Traces tab → search for "ino.handle". Confirm the trace tree shows `ino.handle → travel.recommender.handle → {ino.flight.search, ino.hotel.search, ino.place.search}`.

- [ ] **Step 5: Commit**
```bash
git add src/Core/Neurons/Ino/InoTelemetry.cs \
        src/Core/Neurons/Ino/InoNeuronHandler.cs \
        domains/travel/Ino.Travel/Handlers/*Handler.cs \
        Aspire/ino.Client/OpenTelemetryExtensions.cs
git commit -m "feat(telemetry): ino.* OTel spans across handlers for Aspire dashboard visibility"
```

---

### Task F3: Final full-solution verification

- [ ] **Step 1: Build**
```
dotnet build ino.slnx
```
Expected: green.

- [ ] **Step 2: Full test suite**
```
dotnet test ino.slnx
```
Expected: green, including all existing travel E2Es and the new `TokyoTripPlanningE2E`.

- [ ] **Step 3: Manual demo pass via MCP**
```
mcp__iaw__assistant_chat(message="Plan me 5 days in Tokyo, mid-May, mid-range budget, I like food markets.")
```
Confirm text reply, follow up with "Show me flights", "Pick hotels", "Build itinerary" — expect RFW bytes attached, NeuronId=ino, Aspire traces clean.

- [ ] **Step 4: Browser visual check**
Open `http://localhost:65437`, run the same prompts. Confirm:
- persona orb animates (Rive or CustomPaint)
- chat thread is append-only, cards stack
- itinerary card renders at the end

- [ ] **Step 5: Capture a demo screenshot for the PR body**

Take a screenshot of the browser window with the full stacked thread and save it to `docs/images/2026-04-13-tokyo-demo.png`. Commit.

```bash
git add docs/images/2026-04-13-tokyo-demo.png
git commit -m "docs: Tokyo demo screenshot for PR body"
```

---

## Deferred phases — acceptance-only stubs

These phases are planned at the spec level and ship as follow-up work **after** the demo PR lands. Each gets its own `writing-plans` pass when scheduled.

### Phase D — Live graph persona projector + clarification

**Acceptance:**
- New `InoPersonaProjector` grain subscribes to timeline events, derives in-flight subtree per correlation id, emits `PersonaBrainState` updates via the existing `IPersonaObserver` push path.
- `StreamPersonaState` emits `SearchingFlights + SearchingHotels` simultaneously during a parallel turn (asserted via Playwright gRPC-Web frame intercept).
- `SynapseResult.NeedsClarification(question, options?)` factory lands; ino returns it on ambiguity; Flutter renders a `ClarificationCard`; next chat request carries `replyToCorrelationId`; backend resumes.
- Cross-session memory test: start session 1, plan Tokyo with food markets. Close, start session 2, ask for "another weekend getaway" — assistant response references food markets, proving persona state survived via `IPersonaGrain`.
- `InoNeuronHandler` replaces keyword routing with an LLM-driven intent classifier.
- Existing single-silo projector hazard documented; multi-silo fix deferred.

### Phase G — Production polish

**Acceptance:**
- Graceful SerpApi timeout: handler catches `HttpRequestException` + `TaskCanceledException`, returns a friendly empty-state `SynapseResult` with a specific `Verb = "flights.unavailable"` that the RFW template renders as a "try again" card, not a stack trace.
- Empty-state cards for every travel type (no flights, no hotels, no places).
- Follow-up chip suggestions emitted by `TravelRecommenderHandler` as structured payload fields; Flutter renders them after the main card.
- Error telemetry: `ino.errors` metric + span event per failed handler; Aspire dashboard shows the error span with clear error type.
- `InoNeuronHandler`'s `ExtractDestination`/`ExtractDayCount` upgraded to a proper NLP pass (LLM or regex over structured date formats).

### Phase B — Decay consolidation primitive

**Acceptance:**
- New `IDecayConsolidationGrain` with Orleans reminder firing every 6h.
- Sweep reads `NeuronRegistryGrain.Synapses` state, applies the 100 → 30 → 1 schedule based on last-access time, drops `decay == 0` rows.
- `NeuronRegistryGrain.TouchSynapseAsync(id)` boosts decay on read.
- Test with a fake clock fixture jumping 30 days forward — asserts decay values are correctly stepped down.
- Travel flow unchanged (still writes `decay = 100` on every synapse).
- Search engine queries default to `decay >= 30` floor.

---

## Self-review checklist (run at plan completion — findings inline)

1. **Spec coverage** — every section in the spec has at least one task:
   - 2.1 hierarchy → A9 + C4
   - 2.2 chat pipeline → A11
   - 2.3 persona propagation via payload → A1 + A8 + A7 (Phase D hardens)
   - 2.4 graph projection → deferred Phase D stub
   - 2.5 clarification → deferred Phase D stub
   - 2.6 durability → out of scope, stated
   - 3 schema-driven RFW → C1 + C3
   - 4 decay → deferred Phase B stub
   - 5.1 append-only thread → E4 + E5
   - 5.2 Rive + fallback → E1 + E2 + E3
   - 5.3 clarification card → deferred Phase D stub
   - 6 verification → A13 + F3
   - 7 phase list → this plan is the phase list
2. **Placeholder scan** — no "TBD", "TODO", "fill in details". A handful of notes explicitly say "Phase G hardens" or "Phase D replaces" — these are future-phase handoffs, not placeholders in this plan's MVP.
3. **Type consistency** — `SynapseResult` shape matches actual `src/Core/Contracts/SynapseResult.cs`. `Blueprint` record matches `src/Core/Neurons/Neuron.cs:41-54`. `PersonaContext` factory is defined in A1 and used identically in A4, A5, A6, A7, A8, C2. `InoActivityMap` values match Flutter `PersonaEmotion` and are referenced by deferred Phase D.
4. **Notes flagged for execution-time Context7 verification**: exact Orleans keyed-DI API (A0-2), `AIFunctionFactory.Create` overloads (A7), `SMIInput<T>` vs `SMINumber` (A0-4, E2), `ISerpApiProviderService` method signatures (A4). All are marked inline in the relevant task.

Plan complete.
