# Trip-Planning Demo — Continuation Plan

Status: foundational architecture committed (PR #17 merged + Slices 1–2 below). Remaining slices 3–9 implement the actual demo end-to-end.

## What's already on master

- **PR #17 merged** (commit `be0a1f1`) — LlmTier, Ino.Llm.Xai, IChatClientFactory + XaiChatClientFactory, fluent `AddIno().WithLlm<T>()` builder, AddInoChatClients in all silos, OpenTelemetry 1.15.3 bumps.
- **Aggressive package bumps + FluentAssertions removal** (commit `a40ceb0`) — Orleans 10.1.0, Aspire 13.2.4, OTel 1.15.3 (+ Api/Propagators pinned to clear GHSA-g94r-2vxg-569j), xUnit/NSubstitute/gRPC prereleases, FluentAssertions stripped from every test (~400 `.Should()` chains rewritten to `Assert.*`), Microsoft.Playwright 1.59.0 added.
- **CI workflow fixed** (commit `a555fc8`) — restore/build/test point at `POC/ino.slnx`; pack step removed.
- **Slice 1: BddMockChatClientFactory** (commit `f5ea81a`) — `INO_TEST_MODE=true` registers a BDD-mock-backed `IChatClientFactory` that ignores tier and returns a regex-driven mock client. AppHost LLM declarations remain pristine.
- **Slice 2: kernel clarification synapses** (commit `c9d5201`) — `Ino.Core.AskClarification` (implements `IHasRfwPayload`) + `Ino.Core.ProvideClarification`.

## Architectural decisions (locked in)

1. **Conversation grain key = correlation_id.** Every conversation-bearing neuron is activated under its conversation's correlation_id as its grain primary key. Orleans guarantees the same activation across turns; the `Neuron<TEvent>` journal recovers slot-fill state.
2. **Discovery routes by synapse type, not by neuron-id.** Turn 1 (`PlanTripRequest`) and turn 2 (`ProvideClarification`) both use Discovery's canonical-handler lookup. The grain *activation* is pinned by correlation_id, but the *routing* is purely typed.
3. **`ProvideClarification` is canonical at the kernel level.** For v0.1, `TripPlannerNeuron` is the sole canonical handler. Multi-domain clarification routing (Travel + Taxi each having their own handler) is deferred — needs either a typed wrapper (`ProvideClarification<TQuestion>`) or a correlation-id dispatcher.
4. **AppHost stays pristine across dev/prod.** Production declarations (`.WithLlm<Grok4FastNonReasoning>().AsFast()` etc.) are *architectural*, not environment-conditional. Dev variation lives inside the silo's `IChatClientFactory` selection (BDD-mock when `INO_TEST_MODE=true`). No env-var checks in `Program.cs`.
5. **Fake travel data, in-process.** No `tripradar/`, no HTTP. Hardcoded JSON lookup keyed by destination string. Three demo destinations: Tokyo, Paris, NYC.
6. **Browser tests are headed by default.** Plain `dotnet test POC/test/Ino.E2E.Tests` opens a real Chromium window so you can watch the demo run. Every CI system (GitHub Actions, Azure Pipelines, GitLab, CircleCI, etc.) auto-sets `CI=true`, so the fixture flips to headless transparently in CI without extra config. Not an xUnit trait — this is runtime behaviour, not test-selection.

## Remaining slices

### Slice 3 — `TripPlannerNeuron` (replaces `ItineraryComposerNeuron`)

**File**: `POC/domains/travel/Ino.Domains.Travel/Neurons/TripPlannerNeuron.cs` (new). Delete `ItineraryComposerNeuron.cs`.

**Contracts to add** in `Ino.Domains.Travel.Contracts`:
- `TripPlannerEvent` — abstract `record : ISynapse` discriminated union of journal events.
  - `TripPlanningStarted(string Query)` : `TripPlannerEvent`
  - `SlotFilled(string Field, string Value)` : `TripPlannerEvent`
- (Keep `PlanTripRequest`, `FindFlightsRequest`, `FindHotelsRequest`, `FindPlacesRequest`, `*CardResponse`, `ItineraryCardResponse` as today.)

**Class shape**:
```csharp
public sealed class TripPlannerNeuron(
    [FromKeyedServices("journal")] IDurableList<EventEnvelope<TripPlannerEvent>> journal,
    IFirePort firePort,
    ILogger<TripPlannerNeuron> log)
    : Neuron<TripPlannerEvent>(journal),
      INeuron<PlanTripRequest>,
      INeuron<ProvideClarification>
{
    public async Task<NeuronResult> HandleAsync(PlanTripRequest synapse, NeuronContext ctx, CancellationToken ct) { ... }
    public async Task<NeuronResult> HandleAsync(ProvideClarification synapse, NeuronContext ctx, CancellationToken ct) { ... }
}
```

**Slot-filling state machine** (read journal → derive state → act):
- Slots: `destination`, `dates`. Order checked: destination first, then dates.
- On `PlanTripRequest`:
  1. RaiseAsync(`TripPlanningStarted(Query)`).
  2. Try to extract destination + dates from `Query` (regex/keyword on "to X", "Tokyo|Paris|NYC", "next week|tomorrow|YYYY-MM-DD"). For each extracted slot, RaiseAsync(`SlotFilled(field, value)`).
  3. Walk slots in order. First missing slot → return `NeuronResult.Ok().With(askClarificationFor(field))` where the helper builds an `AskClarification` with chip suggestions and pre-rendered RFW (see Slice 5 templates).
  4. All slots filled → call `ComposeItineraryAsync` (the fan-out from current `ItineraryComposerNeuron`, lifted in here).
- On `ProvideClarification`:
  1. RaiseAsync(`SlotFilled(synapse.Field, synapse.Value)`).
  2. Re-walk slots; same logic. Either return next AskClarification or compose itinerary.

**Slot extraction helpers** (private statics):
- `TryExtractDestination(string query)` — match against `["Tokyo", "Paris", "NYC", "New York"]` case-insensitive.
- `TryExtractDates(string query)` — match `"next week"`, `"this weekend"`, `"tomorrow"`, `"\d{4}-\d{2}-\d{2}"`. Return canonical string for the demo (e.g. "next week" stays "next week").

**Suggestion table** (private static):
- `destination` → `["Tokyo", "Paris", "NYC"]`
- `dates` → `["this weekend", "next week", "next month"]`

**ComposeItineraryAsync**: lift the existing `ItineraryComposerNeuron.HandleAsync` body. Pass the slot-filled destination/dates as a synthesized query string ("Tokyo trip, next week") into the FlightSearch/HotelSearch/PlaceSearch fan-out.

**Update `Travel.cs`**: no change to experience declarations — `PlanTripRequest` still maps to `travel.plan-trip`; the canonical handler swap is automatic via Orleans grain-class registration.

**Delete `ItineraryComposerNeuron.cs`** and update `POC/test/Ino.Domains.Tests/ItineraryComposerNeuronTests.cs` → rename to `TripPlannerNeuronTests.cs` and adapt (single-shot test that bypasses clarifications by passing a fully-specified query, plus a clarification round-trip test).

**Build/test**: `dotnet build POC/ino.slnx && dotnet test POC/ino.slnx`. Expect green.

---

### Slice 4 — Extend FakeTravelDataSource

**File**: `POC/domains/travel/Ino.Domains.Travel/SeedData/FlightFixture.cs` (extend; same for HotelFixture, PlaceFixture).

Replace the single `BaliTrip` constant with a destination-keyed dictionary:
```csharp
public static readonly Dictionary<string, FlightSummary[]> ByDestination =
    new(StringComparer.OrdinalIgnoreCase)
    {
        ["Tokyo"] = [new("ANA", "JFK", "HND", 1180, "2026-05-04 13:30", "13h 50m"), ...],
        ["Paris"] = [...],
        ["NYC"] = [...],
    };

public static FlightSummary[] For(string destination) =>
    ByDestination.TryGetValue(destination ?? "", out var flights) ? flights : ByDestination["Tokyo"];
```

Same shape for `HotelFixture.For(destination)` and `PlaceFixture.For(destination)`.

Update `FlightSearchNeuron`/`HotelSearchNeuron`/`PlaceSearchNeuron` to call `FlightFixture.For(synapse.Query)` (and similar). The query passed by `TripPlannerNeuron.ComposeItineraryAsync` should be just the destination so the lookup hits.

**Test**: existing `FlightSearchNeuronTests` etc. stay; add one parameterized test per neuron asserting Tokyo/Paris/NYC each yield non-empty results.

---

### Slice 5 — Three RFW templates

Pattern to follow: `POC/domains/travel/Ino.Domains.Travel/UI/FlightCardTemplate.cs`. RFW DSL is text written into UTF-8 bytes (LF-only — never CRLF). The Flutter side (`POC/clients/ino.flutter/lib/ui/components/flight_card.dart`) hosts the matching widget.

1. **`ClarificationChipsTemplate.cs`** (new) — emits a chip row that fires `ProvideClarification` on tap. RFW's `Event` widget surface in the Dart runtime is the binding — when a chip is tapped, Flutter receives an event named `ino:provide-clarification` carrying `{ field, value }`. The Flutter chat screen handler converts that event into a `FireSynapse` gRPC call (Slice 6).
   ```
   import core.widgets;
   widget root = Column(children: [
     Text(text: data.prompt, style: { fontSize: 16.0 }),
     SizedBox(height: 12.0),
     Wrap(
       spacing: 8.0, runSpacing: 8.0,
       children: [
         GestureDetector(
           onTap: event "ino:provide-clarification" { field: data.field, value: data.suggestions.0 },
           child: Chip(label: Text(text: data.suggestions.0))
         ),
         ... (one per suggestion; emit count from C# loop, same pattern as FlightCardTemplate's `for (var i = 0; i < count; i++)`)
       ]
     )
   ]);
   ```
   Data payload:
   ```json
   { "prompt": "When are you going?", "field": "dates", "suggestions": ["this weekend","next week","next month"] }
   ```

2. **`ItineraryTimelineTemplate.cs`** (new) — vertical timeline of day cards combining flight + hotel + places. Follow the existing `ItineraryCardTemplate.cs` pattern as a reference, but with chip-style "Replace flight" / "Replace hotel" buttons that emit `event "ino:provide-clarification"` with `field=flight_choice` etc. (out-of-scope for the demo's first cut — start with read-only timeline).

3. **`flight_card.rfwtxt`/`hotel_card.rfwtxt`** — already exist as `FlightCardTemplate`/`HotelCardTemplate`. Confirm they render correctly during E2E.

---

### Slice 6 — Flutter chat screen wiring

**Files to read first**: `POC/clients/ino.flutter/lib/screens/home/home_screen.dart`, `POC/clients/ino.flutter/lib/grpc/generated/ino.pb.dart`, `POC/clients/ino.flutter/pubspec.yaml`.

Add the `rfw` Flutter package if not already present. Wire:
1. **Receive RFW**: when `ChatResponse` with `rfw_description` lands, render the RFW widget tree using the `rfw` package's `RemoteWidget` against a parsed `RemoteWidgetLibrary`. The library should bind:
   - Standard widgets (`Column`, `Wrap`, `GestureDetector`, `Chip`, `Text`, `SizedBox`).
   - The `core.widgets` import maps to local Flutter widgets.
   - The `ino.flights` import maps to a `FlightCard` Dart widget already present in the codebase (verify).
2. **Event dispatch**: register an event handler that catches events named `ino:provide-clarification` (and any other `ino:*`). The handler:
   - Extracts `arguments` (e.g. `{field, value}`).
   - Calls `FireSynapse` gRPC with `verb="ino.core.provide-clarification"`, `args=arguments`, `target_neuron=null`. (Verb naming TBD — gateway side maps verb → typed synapse. See `POC/src/Ino.Gateway.Grpc/Services/InoGrpcService.cs` for the existing FireSynapse handler.)
   - Includes the conversation's correlation_id (cached from the previous `ChatResponse`'s metadata — see Slice 6.3).
3. **Correlation-id caching**: extend `home_screen.dart`'s chat state to track the *last seen* correlation_id from any inbound `ChatResponse`. The current proto doesn't carry correlation_id explicitly — **action**: add `correlation_id` to `ChatResponse` and `FireRequest` proto fields, regenerate both Dart and C# stubs, update the gateway to populate/read.

Gateway-side `FireSynapse` mapping (`InoGrpcService.cs`):
```csharp
case "ino.core.provide-clarification":
    var synapse = new ProvideClarification(
        Field: req.Args["field"],
        Value: req.Args["value"]);
    var ctx = BuildCtx(correlationId: req.CorrelationId);
    var result = await firePort.Fire(synapse, ctx, ct);
    return new FireResponse { Ok = result.Success, SynapseId = ctx.SynapseId.Value };
```

---

### Slice 7 — BDD scenarios

**File**: `POC/domains/travel/Ino.Domains.Travel/Features/travel-intent.feature` (extend).

Add three scenarios:
```gherkin
Scenario: Plan trip to Tokyo with dates inline
  Given the user says "plan.*trip.*Tokyo.*next week"
  Then the assistant replies "Planning your Tokyo trip for next week — composing flights, hotels, and places."

Scenario: Plan trip to Tokyo without dates
  Given the user says "plan.*trip.*Tokyo$"
  Then the assistant replies "Planning a Tokyo trip — when are you going?"

Scenario: Plan trip without destination
  Given the user says "plan.*trip$"
  Then the assistant replies "Planning a trip — where would you like to go?"
```

These power Cortex's intent classification when `INO_TEST_MODE=true`. The TripPlannerNeuron's slot extraction (Slice 3) doesn't depend on Cortex's reply — it parses the raw query — but Cortex's reply text is what the inspector Reasoning panel surfaces.

---

### Slice 8 — Playwright experience-test fixture

**File**: `POC/test/Ino.E2E.Tests/InoExperienceFixture.cs` (new).

```csharp
public sealed class InoExperienceFixture : IAsyncLifetime
{
    public DistributedApplication App { get; private set; } = null!;
    public IBrowser Browser { get; private set; } = null!;
    public IPage Page { get; private set; } = null!;
    public string SystemSiloUrl { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Environment.SetEnvironmentVariable("INO_TEST_MODE", "true");

        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Ino_AppHost>();
        App = await builder.BuildAsync();
        await App.StartAsync();
        await App.ResourceNotifications.WaitForResourceHealthyAsync(KernelSilo.System.ToResourceName());
        await App.ResourceNotifications.WaitForResourceHealthyAsync(KernelSilo.Identity.ToResourceName());
        await App.ResourceNotifications.WaitForResourceHealthyAsync(KernelSilo.Domains.ToResourceName());

        SystemSiloUrl = App.GetEndpoint(KernelSilo.System.ToResourceName(), "https").ToString();

        var pw = await Playwright.CreateAsync();
        var headless = string.Equals(
            Environment.GetEnvironmentVariable("CI"),
            "true",
            StringComparison.OrdinalIgnoreCase);
        Browser = await pw.Chromium.LaunchAsync(new() { Headless = headless });
        var ctx = await Browser.NewContextAsync(new() { IgnoreHTTPSErrors = true });
        Page = await ctx.NewPageAsync();
        await Page.GotoAsync(SystemSiloUrl);
    }

    public async ValueTask DisposeAsync()
    {
        await Browser.DisposeAsync();
        await App.DisposeAsync();
        Environment.SetEnvironmentVariable("INO_TEST_MODE", null);
    }
}

[CollectionDefinition(nameof(InoExperienceCollection))]
public sealed class InoExperienceCollection : ICollectionFixture<InoExperienceFixture> { }
```

Add `Microsoft.Playwright` PackageReference + the `playwright install chromium` post-restore target to `Ino.E2E.Tests.csproj`. (CPM `Microsoft.Playwright 1.59.0` is already in `Directory.Packages.props`.)

---

### Slice 9 — Two experience tests

**File**: `POC/test/Ino.E2E.Tests/TripPlanningExperienceTests.cs` (new).

```csharp
[Collection(nameof(InoExperienceCollection))]
public class TripPlanningExperienceTests(InoExperienceFixture fx)
{
    [Fact]
    public async Task plan_trip_to_tokyo_next_week_renders_itinerary()
    {
        var input = fx.Page.Locator("input[type=text]").First;
        await input.FillAsync("plan a trip to Tokyo next week");
        await input.PressAsync("Enter");

        // Itinerary timeline rendered as RFW
        await fx.Page.WaitForSelectorAsync("text=Tokyo");
        await fx.Page.WaitForSelectorAsync("text=ANA"); // first flight from FlightFixture.For("Tokyo")
        await fx.Page.WaitForSelectorAsync("text=Departure"); // last day of itinerary
    }

    [Fact]
    public async Task plan_trip_to_tokyo_asks_for_dates_then_renders()
    {
        var input = fx.Page.Locator("input[type=text]").First;
        await input.FillAsync("plan a trip to Tokyo");
        await input.PressAsync("Enter");

        // Clarification chip row should appear
        var nextWeekChip = fx.Page.Locator("text=next week");
        await nextWeekChip.WaitForAsync();
        await nextWeekChip.ClickAsync();

        await fx.Page.WaitForSelectorAsync("text=ANA");
    }
}
```

Run with: `dotnet test POC/test/Ino.E2E.Tests` (no args; the experience tests run headed).

For a single test: `dotnet test POC/test/Ino.E2E.Tests --filter "FullyQualifiedName~TripPlanning"`.

CI is automatic — every CI runner sets `CI=true` so the fixture launches headless without config changes.

---

## Execution order

Slices are listed in dependency order — each builds on the previous. Suggested commit-per-slice with `git push origin master` after each green build+test.

1. Slice 3 — TripPlannerNeuron (foundation; no UI yet, but neuron tests pass).
2. Slice 4 — FakeTravelDataSource (lets TripPlanner produce real itineraries for Tokyo/Paris/NYC).
3. Slice 7 — BDD scenarios (single feature-file commit, low risk).
4. Slice 5 — RFW templates (the chip template is the new bit; itinerary timeline is an evolution of the existing `ItineraryCardTemplate`).
5. Slice 6 — Flutter chat screen wiring + proto regeneration. **Highest risk slice** — needs proto evolution, dart codegen, and live testing in a browser.
6. Slice 8 — Playwright fixture.
7. Slice 9 — Experience tests. Run them and fix until green.

After Slice 9 green: `INO_TEST_MODE=true aspire run` → open the system silo HTTPS URL → type "plan a trip to Tokyo next week" → see itinerary card. End-to-end, BDD-mocked LLM, faked data, real RFW → real Flutter rendering, real correlation_id round-trip on clarifications.

## Known traps to watch

- **RFW DSL rejects CRLF**. Build description bytes by appending `'\n'` literals; never `Environment.NewLine`. `FlightCardTemplate.BuildListDescription` shows the pattern.
- **Orleans grain-class registration on multi-interface neurons**. `TripPlannerNeuron` implements `INeuron<PlanTripRequest>` AND `INeuron<ProvideClarification>`. Discovery should pick up both by reflection — `DomainRegistrar` already enumerates `INeuron<>` interfaces. Verify by checking `/discovery/table` after silo boot.
- **`ProvideClarification` collision**. v0.1 has only one canonical handler. If a test tries to register a second neuron as canonical for `ProvideClarification`, Discovery will throw `DiscoveryConflictException` — that's correct behaviour; the test is wrong.
- **`InoTestAppHost` doesn't set INO_TEST_MODE**. The existing fixture in `Ino.Testing` boots the full AppHost. Either set the env var inside that fixture's `InitializeAsync` (cleanest) or have `InoExperienceFixture` set it before calling `DistributedApplicationTestingBuilder.CreateAsync` (per the example above).
- **Aspire `WaitForResourceHealthyAsync` after `DistributedApplicationTestingBuilder`**. Aspire 13.2.4 requires all three silos to reach Healthy or the page navigation will get connection refused. The fixture template above waits for all three explicitly.
- **`grainClassNamePrefix` trap (CLAUDE.md)**. When `IFirePort.Fire<INeuron<T>>` resolves a grain, never pass `Type.FullName` as the prefix. Interface-only resolution.

## What success looks like

```bash
INO_TEST_MODE=true aspire run
# → open https://localhost:<port>/ in browser
# → type "plan a trip to Tokyo next week", hit Enter
# → see Tokyo itinerary card with ANA flight, hotel, places

dotnet test POC/test/Ino.E2E.Tests
# → 2 passed, headed Chromium opens during test, asserts pass
# → in any CI (CI=true is auto-set), runs headless transparently
```
