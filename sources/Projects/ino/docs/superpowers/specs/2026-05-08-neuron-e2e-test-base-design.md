# NeuronE2E test framework — design

**Date:** 2026-05-08
**Status:** Design approved; pending implementation plan
**Author:** Vladyslav Horbachov + Claude

## Problem

ino's E2E test infrastructure is fragmented across two assemblies, four
collection types, and a generic chain that threads the AppHost type through
three layers before it reaches a test. Adding a new domain requires editing
hard-coded lists in `InoTestAppHost.cs` (port allocation, resource health
checks). Each test class re-derives the same boilerplate — `GrpcChannel.For
Address` with a self-signed cert handler, a `SendChatAsync` stream-drain
helper, a `FireRfwEventAsync` helper, a `UniqueUserId()` helper, a
`GrpcResponseCapture` body-intercept class. The browser path
(`InoBrowserFixture`) extends the gRPC path so every E2E test pays the
Chromium cost even when no UI is involved.

The system's primitives are **neurons** (agents) and **synapses** (verbs
they handle or emit). Tests should speak that vocabulary directly. They
don't today.

## Goals

1. **One primitive per concept.** A test targets one neuron. A test
   exercises synapses. There are no other test types — no `BrowserTest`,
   `ShowcaseTest`, or `UnitTest` peers.
2. **`dotnet test --filter "Neuron=<id>"` runs that neuron's tests.** The
   filter is the contract; everything else is plumbing.
3. **BDD-as-TDD.** `.feature` files describe synapse choreography. New
   behavior starts as a scenario; implementation follows. Coverage falls
   out for free because uncovered behavior doesn't exist as a scenario.
4. **Visual proof on demand.** Tests can pop a real Chromium window
   (headed locally, headless in CI) attached to the test's session, so the
   developer sees the RFW cards render. Multiple browser scenarios in one
   `dotnet test` run produce multiple Chrome instances — that's expected.
5. **AppHost discovers itself.** Adding a new domain to `Ino.AppHost.cs`
   adds it to the test fixture automatically. No hard-coded resource
   lists.
6. **Drop-in migration.** Old infrastructure stays compiling and passing
   throughout the rollout. CI never goes red on a coordinated commit.

## Non-goals

- Replacing the in-proc Orleans `TestCluster` fixtures
  (`InoTestSiloFixture`, `InoMultiSiloFixture`). Those solve a different
  problem (fast unit-style grain tests with no Aspire boot) and stay.
- Replacing `test/Ino.E2E.Tests`'s platform-level integration tests
  (BrainStream, FireTestSynapse, install flow). Those test the platform,
  not a neuron, and stay on a renamed `InoPlatformTestAppHost`.
- Building a Reqnroll source generator in slice 1. We accept one-line
  boilerplate per test class for now.

## Vocabulary

| Term | Definition |
|------|------------|
| **Neuron** | An agent. Implementation type (e.g. `TripPlanner`) declared in `IDomain.DeclaredNeurons` with a `NeuronId` and a `CanonicalSynapseType`. |
| **Synapse** | A typed message. *Inbound* synapses are routed by Cortex from a chat prompt or fired by UI/system code. *Outbound* synapses are emitted by a neuron during handling. |
| **Session** | The conversation/correlation context for one `[Fact]`. Holds the gateway-stamped `correlationId`, the unique `userId`, the chat frames seen so far, the synapses observed firing, and (optionally) an attached browser page. Disposed at end of test. |
| **Frame** | One gateway-emitted response. Has `ContentType`, optional RFW (description + data), an `IsSkeleton` flag. |

These are the only nouns the test framework exposes. "Plan" is an
implementation strategy, not a primitive — tests never see it.

## Architecture

### Project topology

| Assembly | Status | Purpose |
|----------|--------|---------|
| `src/Ino.NeuronTesting` | **new** | `NeuronE2ETest<TNeuron>`, `NeuronSession`, `ChatFrame`, `RfwPayload`, `NeuronAppHostFixture`. Aspire AppHost + lazy Playwright. |
| `src/Ino.NeuronTesting.Bdd` | **new** | Reqnroll `[Binding]` step library. Speaks neuron/synapse vocabulary, calls `NeuronSession` underneath. |
| `src/Ino.Testing` | **slimmed** | Keeps `InoTestSiloFixture`, `InoMultiSiloFixture`, `BddMockChatClientFactory` (refactored for per-fixture corpus scoping). Drops `InoTestAppHost`, `InoE2ECollection`, `InoTestCollection`, `InoMultiSiloCollection` once migrated. |
| `src/Ino.Testing.E2E` | **retired** | Deleted at end of slice 5. |
| `src/Ino.PlatformTesting` (renamed from part of old Ino.Testing) | **new (rename)** | Hosts the platform-level `InoPlatformTestAppHost` for `Ino.E2E.Tests` (install flow, brain stream, etc.) — not neuron-targeted, kept separate. |

### Folder layout (per domain)

```
domains/travel/
├── TripPlanner/
│   ├── PlanTripRequest.cs              ← synapse contract (verb)
│   ├── TripPlanner.cs                  ← neuron impl (agent)
│   ├── TripPlanner.Tests.cs            ← `public sealed class TripPlannerTests : NeuronE2ETest<TripPlanner> {}`
│   ├── trip-planner.feature            ← BDD scenarios + LLM corpus, tagged @neuron:travel.plan-trip
│   └── Rfw/
│       ├── TripIntroBuilder.cs
│       └── …
├── FlightSearch/
│   ├── FindFlightsRequest.cs
│   ├── FlightSearch.cs
│   ├── FlightSearch.Tests.cs
│   └── flight-search.feature
└── Tests.csproj                         ← one test csproj per domain;
                                          globs `**/*Tests.cs`,
                                          `<EmbeddedResource Include="**/*.feature">`
                                          references Projects.Ino_AppHost
```

One folder per neuron. Synapse contract, agent, tests, BDD corpus, RFW
builders all colocated. The test csproj sits at the domain root and is the
only thing that references `Projects.Ino_AppHost` — production assemblies
stay AppHost-free, so the cycle never forms.

### API — `NeuronE2ETest<TNeuron, TAppHost>`

The base is generic on both the neuron under test AND the AppHost project,
because the AppHost type is what `DistributedApplicationTestingBuilder`
needs at boot. We surface that as an explicit second type parameter
rather than reflecting it from `TNeuron`'s assembly — the explicitness
beats the magic, and per-domain test classes already pin one specific
AppHost (`Projects.Ino_AppHost`) anyway.

```csharp
public abstract class NeuronE2ETest<TNeuron, TAppHost> : IAsyncLifetime
    where TNeuron : class
    where TAppHost : class
{
    protected NeuronId NeuronUnderTest { get; }      // reflected from TNeuron
    protected DistributedApplication App { get; }    // shared per test class
    protected string KernelGrpcUrl { get; }

    protected NeuronSession Open(string? userId = null);
    protected Task<NeuronSession> Chat(string prompt, [CallerMemberName] string testName = "");
}
```

Per-domain test projects shorten the boilerplate with a project-local
intermediate base:

```csharp
// domains/travel/Tests.csproj's _DomainTestBase.cs (one file per domain)
public abstract class TravelNeuronTest<TNeuron> : NeuronE2ETest<TNeuron, Projects.Ino_AppHost>
    where TNeuron : class { }

// domains/travel/TripPlanner/TripPlanner.Tests.cs
public sealed class TripPlannerTests : TravelNeuronTest<TripPlanner> { }
```

That keeps the per-neuron test class to one line, while the AppHost
binding stays explicit at the domain level.

public sealed class NeuronSession : IAsyncDisposable
{
    public string CorrelationId { get; }
    public string UserId { get; }
    public ChatFrame Last { get; }
    public IReadOnlyList<ChatFrame> Frames { get; }
    public IReadOnlyList<SynapseFire> Observed { get; }

    public Task<ChatFrame> Chat(string prompt);
    public Task<ChatFrame> Fire(string eventName, object args);
    public Task<ChatFrame> Fire<TSynapse>(TSynapse synapse) where TSynapse : ISynapse;

    public Task<NeuronPage> OpenBrowser();             // headed locally, headless in CI

    public Task<ChatFrame> WaitForRfw(string contentType, TimeSpan? timeout = null);
    public Task<SynapseFire> WaitForSynapse(string synapseType, TimeSpan? timeout = null);
}

public sealed class ChatFrame
{
    public string ContentType { get; }
    public string Reply { get; }
    public bool IsSkeleton { get; }
    public RfwPayload? Rfw { get; }
}

public sealed class RfwPayload
{
    public string Description { get; }
    public JsonElement Data { get; }
    public bool ContainsWidgets(params string[] widgetNames);
    public T? DataAt<T>(string jsonPath);
}

public sealed class NeuronPage : IAsyncDisposable
{
    public IPage Playwright { get; }                   // escape hatch
    public Task<byte[]> Screenshot();
}
```

The test author sees `Chat`, `Fire`, `OpenBrowser`, `WaitForRfw`,
`WaitForSynapse`, `Last`, `Frames`. Nothing else is needed for 95% of
tests.

### Programmatic example (escape hatch)

```csharp
public sealed class TripPlannerTests : TravelNeuronTest<TripPlanner>
{
    [Fact]
    public async Task initial_plan_emits_intro_card()
    {
        var s = await Chat("plan a trip to Bali next month");
        s.Last.ContentType.Should().Be("ino.travel.intro");
        s.Last.Rfw!.ContainsWidgets("WeatherSummaryCard", "FlightCard")
            .Should().BeTrue();
    }
}
```

The 95% case is Gherkin (next section); `[Fact]` is for property-based
tests, performance assertions, and orchestration patterns Gherkin can't
express linearly.

### BDD step library — `Ino.NeuronTesting.Bdd`

The step library is one `[Binding]` class with reusable Given/When/Then
phrases that wrap `NeuronSession`. Test authors write only `.feature`
files; Reqnroll generates the xUnit `[Fact]`s; the bindings call back
into `NeuronSession`.

```gherkin
@neuron:travel.plan-trip
Feature: TripPlanner

  Scenario: Bali trip — initial card
    When the user says "plan a trip to Bali next month"
    Then the user sees a card with content type "ino.travel.intro"
     And the card includes widgets "WeatherSummaryCard", "FlightCard"

  Scenario: Bali trip — full 6-hop flow
    Given the user said "plan a trip to Bali next month"
    When the user fires "flight.selected" with flightId="FL-001"
    Then the user sees a card with content type "ino.travel.hotels"

    When the user fires "hotel.selected" with hotelId="H-001"
    Then the user sees a card with content type "ino.travel.events"

    When the user fires "event.selected" with eventId="EV-001"
    Then the user sees a card with content type "ino.travel.activities"
     And the card data includes "weatherBadge"

    When the user fires "activity.selected" with activityId="AC-001"
    Then the user sees a card with content type "ino.travel.summary"
     And the card data includes "Bali", "Singapore Airlines"

  @ui
  Scenario: Bali trip — render in browser
    When the user says "plan a trip to Bali next month"
     And the user opens the chat in a browser
    Then the user sees a card with content type "ino.travel.intro"

  Scenario: Weather change suggests indoor activities
    Given the user said "plan a trip to Bali next month"
     And the user fired "flight.selected" with flightId="FL-001"
     And the user fired "hotel.selected" with hotelId="H-001"
    When a "WeatherForecastChanged" synapse arrives with rainProbability=0.85
    Then the user sees a card with content type "ino.travel.activities"
     And the card data includes "weatherBadge=rainy day pick"
```

The `@ui` tag triggers `OpenBrowser()` in the When step. Multiple `@ui`
scenarios in one feature → multiple Chromium tabs in one `dotnet test`
run.

The minimal C# bootstrap per neuron is one line (using the per-domain
intermediate base from above):

```csharp
public sealed class TripPlannerTests : TravelNeuronTest<TripPlanner> { }
```

Reqnroll's generated test class for `trip-planner.feature` references this
type to access the fixture; bindings receive the `NeuronSession` via
xUnit DI.

### TDD workflow

```
1. write a scenario in <neuron>.feature
2. dotnet test --filter "Neuron=<neuron-id>"        → red
3. add the synapse handler / change the implementation
4. dotnet test                                       → green
5. commit
```

Coverage guarantee: every behavior is a scenario; uncovered behavior is
not a scenario; therefore not implemented.

### Fixture lifecycle

**Scope:** one Aspire AppHost instance per `NeuronE2ETest<T>` subclass
(per-test-class collection). Today's setup is collection-shared across the
whole `TripPlanningCollection`; per-class isolation costs more boot time
(~21s today) but eliminates cross-class state bleed.

**Init sequence (replaces `InoTestAppHost.InitializeAsync`):**

```csharp
sealed class NeuronAppHostFixture<TAppHost> : IAsyncLifetime where TAppHost : class
{
    public DistributedApplication App { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Environment.SetEnvironmentVariable("INO_TEST_MODE", "true");

        var builder = await DistributedApplicationTestingBuilder.CreateAsync<TAppHost>();

        foreach (var p in builder.Resources.OfType<ParameterResource>())
            builder.Configuration[$"Parameters:{p.Name}"] = "test";

        App = await builder.BuildAsync();
        await App.StartAsync();

        var siloResources = builder.Resources
            .OfType<ProjectResource>()
            .Where(r => !r.Name.StartsWith("telegram"))
            .Select(r => r.Name);

        await Task.WhenAll(siloResources.Select(name =>
            App.ResourceNotifications.WaitForResourceHealthyAsync(name).AsTask()));
    }
}
```

**What disappears:**
- Hard-coded `kernel`/`identity`/`travel`/`taxi` enumeration. Resources
  come from `builder.Resources.OfType<ProjectResource>()`. Adding a
  domain → no test-infra change.
- Manual TCP port allocator (`InoTestAppHost.cs:71-74` race window).
  `DistributedApplicationTestingBuilder` already isolates ports per
  AppHost instance. The reason ports were env-var-fixed in production
  (stable dashboard URLs across `aspire run`s) doesn't apply to tests.
- `installed.json` / `marketplace.json` temp file plumbing — only
  `Ino.E2E.Tests` install-flow tests need those, scoped there.
- `InoBrowserFixture` extending `InoTestAppHost`. Replaced by lazy
  Playwright instantiation inside `NeuronSession.OpenBrowser()`. No
  Chromium cost when no test asks for a page.

**BDD corpus auto-binding:**
- Feature files become embedded resources
  (`<EmbeddedResource Include="**/*.feature">` in the test csproj).
- Fixture init reflects on `TNeuron` → resolves `NeuronId` via the loaded
  `IDomain` registry → enumerates assembly manifest resources → loads
  every `.feature` text whose tags include `@neuron:<id>` → registers
  with `BddMockChatClientFactory` scoped to this fixture's id.
- `BddMockChatClientFactory` gains a `RegisterCorpusForFixture(fixtureId,
  text)` method (today's static state stays alongside it during
  migration).

## Migration plan — five slices, additive

The new framework lands alongside the old. Existing tests pass throughout.

### Slice 1 — bring up the framework (zero existing tests change)

1. Create `src/Ino.NeuronTesting/` with `NeuronE2ETest<T>`,
   `NeuronSession`, `ChatFrame`, `RfwPayload`, `NeuronPage`,
   `NeuronAppHostFixture<TAppHost>`.
2. Create `src/Ino.NeuronTesting.Bdd/` with the `[Binding]` step library.
3. Refactor `BddMockChatClientFactory` to support per-fixture corpus
   scoping (additive — keep existing static API).
4. Add a smoke test `domains/travel/TripPlanner.Smoke/` exercising the
   "Bali initial card" scenario only. Proves the framework boots, runs
   Gherkin, opens a browser, asserts.

**Exit criteria:** all existing tests still pass; new smoke test passes
in headed and headless modes.

### Slice 2 — migrate Travel domain

1. Move `domains/travel/Ino.Domains.Travel/Plans/PlanTripPlan.cs` →
   `domains/travel/TripPlanner/TripPlanner.cs`. Rename type to
   `TripPlanner`. Adjust `Travel.cs:41` `PlanType = typeof(TripPlanner)`.
2. Move `Neurons/FlightSearchNeuron.cs` → `FlightSearch/FlightSearch.cs`,
   etc. RFW builders move alongside their owning neuron.
3. Create `domains/travel/Tests.csproj`. Globs `**/*Tests.cs`, embeds
   `**/*.feature`. References `Projects.Ino_AppHost` and the new
   `Ino.NeuronTesting` + `Ino.NeuronTesting.Bdd`.
4. Translate test bodies to scenarios:
   - `RichTripPlanningE2ETests.Plan_trip_to_bali_walks_full_six_hop_flow_under_bdd_mocks` → `trip-planner.feature` "full 6-hop" scenario.
   - `RichTripPlanningE2ETests.Plan_trip_can_skip_events_and_still_reach_activities` → "events skipped" scenario.
   - `AskInoRoutingTests` (×3) → `cortex.feature` (kernel domain — handled in slice 4).
   - `TripPlanningNeuronTests` (×2) → `trip-planner.feature` `@ui` scenarios.
5. Once new tests are green and cover the same behaviors, delete
   `domains/travel/Ino.Domains.Travel.Tests/`.

**Exit criteria:** Travel domain tests run via the new framework only;
old test project deleted; CI green.

### Slice 3 — migrate other domains

Parallel-safe per-domain PRs:
- `domains/taxi/Ino.Domains.Taxi.Tests` → `domains/taxi/<Neuron>/`
- `domains/genesis/`, `domains/location/`, `domains/recall/`,
  `domains/reminders/` — same pattern.

**Exit criteria:** all domain test projects migrated; only platform-level
tests remain on the old infra.

### Slice 4 — kernel-level tests

- `test/Ino.Kernel.Tests` — Cortex routing + missed-intent. Mostly
  `TestCluster`-based; those keep `InoTestSiloFixture`. Migrate the
  Aspire-backed ones (the `AskInoRoutingTests` block from Travel slot
  in here as `cortex.feature`).
- `test/Ino.E2E.Tests` (BrainStream, FireTestSynapse, install flow) —
  these are platform-level, not neuron-targeted. Rename
  `InoTestAppHost` → `InoPlatformTestAppHost` in a new
  `src/Ino.PlatformTesting/`. They keep their own collection model.

**Exit criteria:** every test class is on either `NeuronE2ETest<T>`,
`InoTestSiloFixture` (TestCluster), `InoMultiSiloFixture` (TestCluster),
or `InoPlatformTestAppHost` (platform Aspire). No more
`InoTestAppHost`/`InoBrowserFixture`.

### Slice 5 — retire old infra

- Delete `src/Ino.Testing.E2E/`.
- Slim `src/Ino.Testing/` to just `InoTestSiloFixture`,
  `InoMultiSiloFixture`, `BddMockChatClientFactory`,
  `RecordedMockChatClient`, `MockLlmMissException`,
  `NeuronContextForTest`, `TestSiloConfigurator`.
- Remove the static-corpus API on `BddMockChatClientFactory` once no
  caller uses it.

**Exit criteria:** no dead code; `git grep InoTestAppHost
InoBrowserFixture` returns nothing.

## Risk surface

- **Per-fixture BDD corpus scoping refactor** could break tests that rely
  on the current static state. *Mitigation:* keep the static API
  alongside during slices 1–4; remove only in slice 5.
- **Per-class AppHost cost.** Today's collection-shared model boots once
  per collection (~21s). Per-class is N×21s. *Mitigation:* if it bites,
  promote shared scope back via a `[NeuronCollection("travel")]` opt-in
  attribute that pools the AppHost across classes that name the same
  collection. Decide after slice 2 lands.
- **Reqnroll discovery in colocated layout.** Requires verifying the
  Reqnroll codegen finds `.feature` files at the new paths.
  *Mitigation:* the smoke test in slice 1 covers this end-to-end before
  any real tests migrate.
- **Source generator for `[CollectionDefinition]`** is non-trivial.
  *Mitigation:* skip in slice 1; accept one-line boilerplate per test
  class until the boilerplate count justifies a generator.

## Open questions to resolve during implementation

- Exact regex / phrasing for step bindings — refine while writing the
  step library, settle once Travel slice 2 ports cleanly.
- Whether to keep `Tokyo.feature` in the new layout as a high-level
  storyboard test or fold its scenarios into per-neuron features. Decide
  during slice 2.
- Whether `WaitForSynapse` reads from OTel traces, the `IFirePort`
  observability surface, or a new test-only side channel. Lowest-risk
  path is OTel — kernel already exports synapse spans.

## Non-decisions (deferred)

- Source generator for `[CollectionDefinition]`.
- Pool-based AppHost reuse across compatible test classes.
- Property-based testing harness on top of `NeuronSession`.
- Cross-neuron orchestration scenarios spanning multiple `.feature`
  files in a single run.

These are post-migration concerns. The five-slice plan above doesn't need
them.
