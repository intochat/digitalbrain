# ino POC — core primitives (design)

**Date:** 2026-04-14
**Scope:** greenfield POC at `D:\ino\POC\` — **does not modify `D:\ino\src\`**
**Status:** design locked in brainstorming session 2026-04-14; ready for implementation planning
**Goal:** land the production-ready core of ino as an AI-native OS — the neuron + synapse primitives, the kernel silos, the marketplace POC, and an acceptance surface of 10 canonical end-to-end scenarios that together prove the architecture works.

---

## 1. North-star

ino is an AI-native operating system. Its two runtime primitives are **neurons** (units of code that handle typed messages) and **synapses** (the typed messages themselves). Its user-facing product primitive is the **experience** — a NuGet package shipping neurons + synapses + BDD behavior tests + recorded LLM mocks — that users browse, install, and share through a marketplace.

This spec describes **Track A**: the core primitives, three kernel silos, and runtime contracts that every experience (first-party or marketplace) builds against. It also delivers **Track A-bis**: 10 canonical end-to-end scenarios whose passage is the acceptance surface for Track A.

The existing code at `D:\ino\src\` stays untouched. Track A ships a parallel greenfield POC at `D:\ino\POC\` with a fresh solution file, no IAW carry-over, and latest .NET + Orleans + Aspire.

## 2. Scope and track decomposition

The brainstorming session identified eight workstreams. This spec covers Tracks A and A-bis only.

| Track | What | When |
|---|---|---|
| **A** | Core primitives — neuron/synapse contracts, three kernel silos (system, identity, experiences), runtime dispatch, identity silo, causal memory, telemetry, test strategy | **This spec** |
| **A-bis** | 10 canonical AI-native OS scenarios as the acceptance surface | **This spec, section 21** |
| **B** | `ino.new` authoring UX + experience SDK + project templates + local dev loop | Next spec |
| **C** | Self-improvement loop — pattern extraction from Playback + CausationIndex, automated experience authoring, learning from success/failure | Later spec |
| **D** | Flutter client rewire + persona visualization tied to live neuron activity | Deferred (explicitly out of Track A) |
| **E** | IAW legacy cut-list | Folded into Track A (we simply don't bring it over) |
| **F** | Per-silo sandboxing, resource budgets, crash recovery policy | Later spec; Track A defines the hooks |
| **G** | Real marketplace (remote feed, verification, signing, revenue) | **Next spec after Track A — not deferred.** Track A reserves every primitive hook Track G needs. |

### 2.1 Immediate Track G dependencies Track A must reserve

The POC marketplace in Track A is deliberately minimal (HTTP endpoints, pre-compiled bundles flipped on/off). Track G replaces it with a real marketplace. To ensure Track G is purely additive, Track A reserves:

1. **Signed experience metadata** — the shape of the metadata Track G will sign lives in Track A's source-generated `ExperienceMetadata` record. Track G adds a signature field and verification logic; Track A's schema supports the extension.
2. **Stable "what an experience is"** — Track A commits to "an experience is a NuGet package that references `Ino.Core.Hosting`, ships grain classes implementing `INeuron<T>`/`IReactsTo<T>`, contract types implementing `ISynapse`, `.feature` files, and `mocks/llm.recordings.yml`." That shape is public-API frozen after Track A.
3. **`POST /marketplace/install/{id}` endpoint contract** — Track A implements the endpoint; Track G wires it to a real feed. The request/response shape is the public contract.
4. **Capability declaration + consent flow** — Track A implements the consent screen via `[RequiresCapability]` attributes aggregated by the source generator. Track G adds remote-capability review (is the declared capability the one the package actually uses?) but doesn't change the primitive.
5. **Install-time BDD gate** — Track A runs `.feature` files in-process via the `Ino.Testing` harness at install time. Track G inherits this unchanged.

## 3. Vocabulary

Two runtime types, one product type.

| Term | Layer | Meaning |
|---|---|---|
| **Neuron** | Runtime | A unit of code that handles typed synapses. Orleans grain class implementing `INeuron<TSynapse>` for request/response or `IReactsTo<TSynapse>` for fan-out. |
| **Synapse** | Runtime | A typed message passed between neurons. `[GenerateSerializer] record` implementing the `ISynapse` marker interface. |
| **Experience** | Product | The marketplace unit. A NuGet package shipping neurons + synapses + BDD tests + recorded LLM mocks. Users install experiences; authors ship experiences. At runtime an experience is "a folder of neurons and their associated synapses" but the word *experience* never leaks into runtime dispatch code. |

**User class names never carry the `Neuron` or `Synapse` suffix.** The interface tells you what a class is. `TripPlanner`, not `TripPlannerNeuron`. `PlanTrip`, not `PlanTripSynapse`. Framework types (`INeuron<T>`, `ISynapse`, `NeuronContext`, `NeuronResult`) use the primitive names because those types *describe* the primitive.

## 4. Architecture — three kernel silos, peer-to-peer dispatch

Three always-on silos, one hosted silo for installed experiences:

| Silo | Role | Hosts |
|---|---|---|
| **system** | User-facing entry point, session management, **search-over-neurons that is the intent routing layer**, root product façade, marketplace HTTP endpoints | `SystemChatService` (gRPC endpoint for Flutter), `SearchIndexer`, `SearchQuery` neurons, `Playback`, `CausationIndex`, `BranchManager`, `MarketplaceInstaller` |
| **identity** | OAuth vault, credential reuse, consent orchestration, per-experience scoped grants, credential lifecycle | TripRadar-pattern `User` + `UserProfile` + new `ExternalGrant` entity; `ExternalOAuthOrchestrator` neurons per provider; Postgres-backed |
| **experiences** | Every installed experience's grains | Every `INeuron<T>` / `IReactsTo<T>` implementation from every installed experience. Redis-backed grain storage via `Microsoft.Orleans.Persistence.Redis`. |

**There is no router silo.** Cross-silo dispatch is direct peer-to-peer gRPC between silos, with endpoints discovered at runtime via a `DiscoveryGrain` in the `system` silo. See section 11.

**There is no timeline silo.** Each neuron's event journal is its own memory, stored via Orleans JournaledGrain + `LogStorage`. Playback across neurons happens via a `Playback` + `CausationIndex` neuron pair living in the `system` silo. See section 13.

## 5. POC solution layout

```
D:\ino\POC\
├── ino.slnx
├── Directory.Build.props
├── Directory.Packages.props
├── global.json                         # pin .NET 10 SDK
├── nuget.config
├── README.md
│
├── src/
│   ├── Ino.Core/                       # primitives: ISynapse, INeuron<T>, IReactsTo<T>,
│   │                                   #             NeuronContext, NeuronResult, Capability,
│   │                                   #             EventEnvelope<T>, attributes
│   ├── Ino.Core.Hosting/               # AddIno(), AddExperience<T>(), AddExperiences<T>(),
│   │                                   #   Neuron<TState,TEvent> base class,
│   │                                   #   ctx.Fire<T>()/ctx.FireBroadcast<T>() runtime,
│   │                                   #   ctx.Search/Identity facades, IAmbientFire,
│   │                                   #   source generator, discovery client
│   ├── Ino.Core.Hosting.Analyzers/     # Roslyn analyzer rules INO001-INO008
│   ├── Ino.System/                     # system silo: user entry, session, search engine,
│   │                                   #   Playback + CausationIndex + BranchManager,
│   │                                   #   MarketplaceInstaller + HTTP endpoints
│   ├── Ino.Identity/                   # identity silo — hosts neurons for auth
│   ├── Ino.Identity.Domain/            # DDD aggregate + entity (lifted from TripRadar shape)
│   ├── Ino.Identity.Infrastructure/    # EF Core + Postgres + OAuth orchestrators
│   ├── Ino.Experiences/                # experiences silo — hosts all installed experience grains
│   ├── Ino.Testing/                    # shared test harness: InoTestHost, RecordedMockChatClient,
│   │                                   #   InoTestContext, Reqnroll bindings, stub IIdentityVault
│   └── Ino.AppHost/                    # Aspire — composes system + identity + experiences
│                                       #   + Postgres resource + Redis resource
│                                       #   reads ~/.ino/installed.json for conditional experience wiring
│
├── contracts/                          # framework-level shared contracts only
│   ├── Ino.Contracts.System/           # UserIntent, SessionStarted, IntentResolved,
│   │                                   #   SearchQuery, BrowserOpenRequested, ConsentRequested, ...
│   ├── Ino.Contracts.Identity/         # IdentityGranted, IdentityRevoked, ReauthenticationRequired,
│   │                                   #   ExternalGrantAdded
│   └── Ino.Contracts.Playback/         # WalkBackwardRequest, WalkForwardRequest,
│                                       #   CorrelationTraceRequest, EventLinked, CausalChain
│
├── experiences/                        # vertical slices — one folder per experience bundle
│   ├── notes/
│   │   ├── Ino.Notes/                  # meta-package: one marker class, references Ino.Notes.Manager
│   │   ├── manager/
│   │   │   ├── Ino.Notes.Manager/              # implementation
│   │   │   ├── Ino.Notes.Manager.Contracts/    # CreateNote, ListNotes, DeleteNote, NoteCreated
│   │   │   └── Ino.Notes.Manager.Tests/        # xunit.v3 + Reqnroll + recorded mocks
│   │   └── README.md
│   │
│   └── travel/
│       ├── Ino.Travel/                         # meta-package — references the 5 travel experiences
│       ├── flight-search/
│       │   ├── Ino.Travel.FlightSearch/
│       │   ├── Ino.Travel.FlightSearch.Contracts/
│       │   └── Ino.Travel.FlightSearch.Tests/
│       ├── hotel-search/
│       │   ├── Ino.Travel.HotelSearch/
│       │   ├── Ino.Travel.HotelSearch.Contracts/
│       │   └── Ino.Travel.HotelSearch.Tests/
│       ├── place-discovery/
│       │   ├── Ino.Travel.PlaceDiscovery/
│       │   ├── Ino.Travel.PlaceDiscovery.Contracts/
│       │   └── Ino.Travel.PlaceDiscovery.Tests/
│       ├── trip-planner/
│       │   ├── Ino.Travel.TripPlanner/         # references all 4 other travel .Contracts NuGets
│       │   ├── Ino.Travel.TripPlanner.Contracts/
│       │   └── Ino.Travel.TripPlanner.Tests/
│       ├── auto-check-in/
│       │   ├── Ino.Travel.AutoCheckIn/
│       │   ├── Ino.Travel.AutoCheckIn.Contracts/
│       │   └── Ino.Travel.AutoCheckIn.Tests/
│       └── README.md
│
└── test/
    ├── Ino.Core.Tests/                 # L1 — pure primitive units, no Orleans
    ├── Ino.System.Tests/               # L2 — system silo integration via shared TestCluster
    ├── Ino.Identity.Tests/             # L2 — identity silo integration via shared TestCluster
    ├── Ino.Hosting.Tests/               # L2 — ctx.Fire<T>() cross-silo dispatch with fake peer silos
    ├── Ino.Bdd/                         # L4 — cross-experience Reqnroll scenarios
    └── Ino.E2E/                         # L5 — Playwright + Aspire AppHost, 10 canonical scenarios
```

## 6. Tech stack

| Layer | Choice | Rationale |
|---|---|---|
| Runtime | **.NET 10** (LTS, released 2025-11) | Latest LTS, `TimeProvider` for virtual test clock, primary constructors |
| Actor model | **Microsoft.Orleans** (latest 9.x or 10.x — verify via Context7 during implementation) | Virtual actor model, grain persistence, reminders, event sourcing |
| Orchestrator | **.NET Aspire** (latest) | Composes the four silos + Postgres + Redis into one dev/prod stack, hot restarts, OTel dashboard |
| Cross-silo RPC | **gRPC** | Typed, fast, HTTP/2 native; used for direct peer-to-peer between silos |
| Grain storage (neuron journals) | **`Microsoft.Orleans.Persistence.Redis`** | Aspire first-class, durable via RDB/AOF, low latency, not ADO.NET |
| Event sourcing | **Orleans `JournaledGrain<TState, EventEnvelope<TEvent>>` + `LogStorage` log consistency provider** | Stays in the Orleans ecosystem; migration path to `CustomStorage` per-neuron if any neuron outgrows LogStorage. See section 10. |
| Identity storage | **PostgreSQL** (lifted from TripRadar pattern) | EF Core + BCrypt; TripRadar's `User` + `UserProfile` + new `ExternalGrant` entity |
| Testing | **xunit.v3** + **Reqnroll** (BDD) + **Playwright** (L5 E2E) | xunit.v3 for speed and fixture lifecycle; Reqnroll for install-time behavior gate; Playwright for browser verification (stubbed in Track A) |
| Telemetry | **OpenTelemetry** | OTLP/gRPC export to Aspire dashboard; three-layer contract reserved, only layer 1 (local) ships |
| Central package management | **`Directory.Packages.props`** | One version truth |
| Analyzer | **`Ino.Core.Hosting.Analyzers`** (custom Roslyn) | Enforces "no cross-experience grain calls outside `ctx.Fire<T>()`" at build time |

## 7. Core contracts — `Ino.Core`

Every contract here is the **public plugin API**. Third-party experience authors build against these types and nothing else from `Ino.Core`.

### 7.1 `ISynapse` — the payload marker

```csharp
namespace Ino.Core;

// Marker interface on every cross-neuron payload record.
// Used as the generic constraint on INeuron<T>, IReactsTo<T>, and ctx.Fire<T>
// so the compiler rejects passing arbitrary types as synapse payloads.
public interface ISynapse { }
```

Payload records look like this:

```csharp
[GenerateSerializer]
public sealed record CreateNote(
    [property: Id(0)] string Text,
    [property: Id(1)] DateTimeOffset CreatedAt) : ISynapse;
```

Contract records ship in small `*.Contracts` NuGet packages that expose nothing but the records. Subscribers reference only contract packages, never implementation packages. Compile-time type resolution IS the schema registry.

### 7.2 `INeuron<T>` — canonical handler interface

```csharp
namespace Ino.Core.Hosting;

// Canonical handler — exactly one implementation per synapse type across
// all installed experiences (duplicate = install rejection).
// ctx.Fire<T>() routes here. Returns NeuronResult synchronously.
public interface INeuron<TSynapse> : IGrainWithStringKey
    where TSynapse : ISynapse
{
    Task<NeuronResult> HandleAsync(
        TSynapse synapse,
        NeuronContext ctx,
        CancellationToken ct);
}
```

**One grain class can implement multiple `INeuron<T>` interfaces** to handle multiple synapse types in one place.

### 7.3 `IReactsTo<T>` — reactive fan-out handler

```csharp
// Reactive listener — zero or many implementations per synapse type.
// ctx.FireBroadcast<T>() delivers in parallel to all of these.
// No aggregate return value — one listener's failure doesn't fail the broadcast.
public interface IReactsTo<TSynapse> : IGrainWithStringKey
    where TSynapse : ISynapse
{
    Task ReactAsync(
        TSynapse synapse,
        NeuronContext ctx,
        CancellationToken ct);
}
```

The **two-interface split** exists because request/response and fan-out have fundamentally different semantics. `Fire<T>()` has a typed return value so it needs exactly one target; `FireBroadcast<T>()` is fire-and-forget and wants arbitrary fan-out. Install-time collision detection applies only to `INeuron<T>`.

Examples across the travel cluster:

| Class | Implements | Role |
|---|---|---|
| `Travel.TripPlanner.TripPlanner` | `INeuron<PlanTrip>` | THE handler for user trip requests |
| `Travel.FlightSearch.SerpFlightSearch` | `INeuron<SearchFlights>` | THE handler for flight searches |
| `Travel.AutoCheckIn.Watcher` | `IReactsTo<TripPlanned>`, `INeuron<PerformCheckIn>` | Listens to trip events + handles scheduled check-ins |
| `Calendar.EventCreator` | `IReactsTo<TripPlanned>`, `IReactsTo<CheckInCompleted>` | Passive listener, creates calendar entries |

### 7.4 `NeuronContext` — the per-call context

```csharp
namespace Ino.Core.Hosting;

public interface NeuronContext
{
    // Identity of the current handled synapse
    string SynapseId { get; }
    string CorrelationId { get; }
    string SourceExperience { get; }
    string SourceStream { get; }       // source grain key — used for causation metadata

    // User context (null when this is part of a background/ambient chain)
    string? UserId { get; }
    string? SessionId { get; }

    // The ONLY cross-neuron primitives — capability-checked, traced, journaled.
    Task<NeuronResult> Fire<T>(T synapse, CancellationToken ct = default) where T : ISynapse;
    Task FireBroadcast<T>(T synapse, CancellationToken ct = default) where T : ISynapse;

    // Facades to kernel silos
    ISearchFacade Search { get; }
    IIdentityFacade Identity { get; }

    // Telemetry + logging — auto-correlated with the current synapse chain
    ILogger Logger { get; }
    Activity? CurrentActivity { get; }
}
```

Every method on `ctx` is auto-instrumented (OTel span + timeline event) and capability-checked against the calling experience's declared `[RequiresCapability]` set.

### 7.5 `NeuronResult` — the return type

```csharp
[GenerateSerializer]
public sealed record NeuronResult(
    [property: Id(0)] bool Success,
    [property: Id(1)] string? Message = null,
    [property: Id(2)] SynapseError? Error = null,
    [property: Id(3)] ISynapse? ResponsePayload = null,
    [property: Id(4)] RfwDescription? Rfw = null)
{
    public static NeuronResult Ok(string? message = null) => new(true, message);
    public static NeuronResult Fail(SynapseError error) => new(false, error.Message, error);

    public NeuronResult With<T>(T payload) where T : ISynapse
        => this with { ResponsePayload = payload };
    public NeuronResult WithRfw(RfwDescription rfw) => this with { Rfw = rfw };

    public bool TryGetPayload<T>(out T payload) where T : ISynapse
    {
        if (ResponsePayload is T typed) { payload = typed; return true; }
        payload = default!;
        return false;
    }
}

[GenerateSerializer]
public sealed record SynapseError(
    [property: Id(0)] string Code,
    [property: Id(1)] string Message,
    [property: Id(2)] IReadOnlyDictionary<string, string>? Details = null);
```

`Rfw` is an optional Remote Flutter Widget rendering description — experiences that want rich cards set it, others leave it null. Track A reserves the field; Track D wires the actual Flutter rendering.

### 7.6 `Capability` — typed discriminated union

```csharp
public abstract record Capability
{
    public sealed record Http(params string[] AllowedHosts) : Capability;
    public sealed record Llm(LlmTier Tier = LlmTier.Default) : Capability;
    public sealed record Persistence(string StoragePrefix) : Capability;
    public sealed record Identity(string Provider, params string[] Scopes) : Capability;
    public sealed record LocalFile(string PathPattern) : Capability;
}

public enum LlmTier { None, Default, Reasoning, Multimodal }
```

Declared on grain classes via attributes:

```csharp
[RequiresCapability(typeof(Capability.Llm), LlmTier.Reasoning)]
[RequiresCapability(typeof(Capability.Persistence), "trip-planner")]
public sealed class TripPlanner : Neuron<TripPlannerState, TripPlannerEvent>,
    INeuron<PlanTrip>
{ ... }
```

The source generator aggregates all `[RequiresCapability]` attributes across the experience assembly into the `ExperienceMetadata` record the marketplace endpoints read for consent prompts.

### 7.7 `[UserEntry]` and `[RequiresCapability]` attributes

```csharp
// Marks a synapse as a user-invocable intent (reachable from the user's
// natural-language input via level-1 search). Indexed at install time
// into the system silo's intent classifier.
[AttributeUsage(AttributeTargets.Class)]
public sealed class UserEntryAttribute : Attribute { }

// Declared on grain classes. Aggregated by the source generator into
// ExperienceMetadata.RequiredCapabilities.
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class RequiresCapabilityAttribute : Attribute
{
    public RequiresCapabilityAttribute(Type capabilityType, params object?[] args) { ... }
}
```

## 8. Experience model

### 8.1 What an experience is, concretely

A NuGet package containing exactly these things:

1. **Grain classes** inheriting `Neuron<TState, TEvent>` and implementing one or more `INeuron<T>` / `IReactsTo<T>` interfaces.
2. **`.Contracts` sibling NuGet** containing the `[GenerateSerializer]` records implementing `ISynapse`.
3. **`.feature` files** (Gherkin) + step definitions using `Ino.Testing.InoTestContext`.
4. **`mocks/llm.recordings.yml`** — recorded LLM responses keyed by prompt fragments.
5. **Standard NuGet metadata** — `PackageId`, `Version`, `Description`, `Authors`, `PackageTags`.

That's the full contract. No manifest file. No custom descriptor. The `.csproj` metadata + attribute-driven source generation is the manifest.

### 8.2 `AddExperiences<T>()` — plural, bundle-based

Experiences compose at AppHost build time via a single extension method per bundle:

```csharp
using Ino.Hosting;
using Ino.Bundles;

var builder = DistributedApplication.CreateBuilder(args);

var ino = builder.AddIno("ino");      // composes system + identity + experiences + Postgres + Redis

ino.AddExperiences<Notes>();          // marker class from the Ino.Notes meta-package
ino.AddExperiences<Travel>();         // marker class from the Ino.Travel meta-package

builder.Build().Run();
```

**Bundle markers live in `Ino.Bundles.*` namespace** to avoid colliding with the implementation namespaces (`Ino.Travel.*`). Each meta-package is a thin NuGet containing:

```csharp
// Ino.Travel meta-package (references the five implementation packages)
namespace Ino.Bundles;
public sealed class Travel { }        // empty marker — only its assembly attribute matters
```

`AddExperiences<T>()` at build time:
1. Finds the assembly containing `T`.
2. Walks the current app domain's loaded assemblies and picks every assembly whose name shares the implementation prefix of `T`'s containing NuGet ID (so `Ino.Travel` matches `Ino.Travel.FlightSearch`, `Ino.Travel.TripPlanner`, etc.).
3. Scans each matching assembly for types implementing `INeuron<>` or `IReactsTo<>`.
4. Registers each grain class with the `experiences` silo configuration.
5. Aggregates `[RequiresCapability]` attributes.
6. Registers the grain assembly's source-generated `ExperienceMetadata` for capability consent and marketplace listing.

A singular `AddExperience<T>()` also exists for pinning a specific marker type; `AddExperiences<T>()` is the default because the bundle model is preferred.

### 8.3 Source-generated `ExperienceMetadata`

The `Ino.Core.Hosting.SourceGenerator` runs at compile time inside the experience project. Reads:
- `[assembly: InoExperience(...)]` if present, else falls back to `.csproj` `PackageId` / `Description` / `PackageTags`.
- `[RequiresCapability]` attributes on all grain classes.
- `[UserEntry]` attributes on all `ISynapse` records.
- All types implementing `INeuron<>` and `IReactsTo<>`.

Emits a static `ExperienceMetadata` field on the bundle's marker class. The author never sees this file:

```csharp
// Generated — do not edit
namespace Ino.Travel;

public sealed partial class TripPlanner    // not a grain — the assembly marker class
{
    public static readonly ExperienceMetadata Metadata = new(
        ExperienceId: "Ino.Travel.TripPlanner",
        Version: "1.0.0",
        Description: "Plan trips with flights, hotels, and activities.",
        Keywords: ["travel", "trip", "flight", "hotel", "itinerary"],
        CanonicalNeurons: new[]
        {
            new CanonicalNeuronInfo(
                SynapseType: "Ino.Travel.TripPlanner.Contracts.PlanTrip",
                GrainType: "Ino.Travel.TripPlanner.TripPlanner",
                IsUserEntry: true)
        },
        ReactiveNeurons: Array.Empty<ReactiveNeuronInfo>(),
        UserEntrySchemas: new[] { "Ino.Travel.TripPlanner.Contracts.PlanTrip" },
        RequiredCapabilities: new[]
        {
            "Llm:Reasoning",
            "Persistence:trip-planner"
        },
        CoreVersion: "0.1.0");
}
```

**Build-time validation** in the generator:
- Duplicate `INeuron<T>` within the same assembly → compile error (`INO002`).
- `[UserEntry]` on a type that doesn't implement `ISynapse` → compile error (`INO003`).
- Missing `[GenerateSerializer]` on a public payload record → compile warning (`INO006`).
- `INeuron<T>` or `IReactsTo<T>` on a non-`sealed` class → compile error (`INO007`).

## 9. The `Neuron<TState, TEvent>` base class

Every experience's grain classes inherit from `Neuron<TState, TEvent>` (in `Ino.Core.Hosting`). The base class wraps Orleans' `JournaledGrain` with:
- Causation envelope injection (so every stored event carries `caused_by_*` metadata)
- `RaiseAsync` + `Apply` semantics (identical to classic JournaledGrain)
- `GetHistoryAsync` for memory retrieval
- A common `IJournaledNeuronQuery` interface for the `Playback` neuron to traverse the journal

```csharp
namespace Ino.Core.Hosting;

[LogConsistencyProvider(ProviderName = "LogStorage")]
[StorageProvider(ProviderName = "NeuronStore")]
public abstract class Neuron<TState, TEvent> :
    JournaledGrain<TState, EventEnvelope<TEvent>>,
    IJournaledNeuronQuery
    where TState : class, new()
    where TEvent : class, ISynapse
{
    INeuronContextAccessor _contextAccessor = null!;   // injected via OnActivateAsync

    // Author-facing: raise a typed event, update projected state, handle concurrency.
    protected async Task RaiseAsync(TEvent @event, CancellationToken ct = default)
    {
        var ctx = _contextAccessor.Current;
        var envelope = new EventEnvelope<TEvent>(
            Payload: @event,
            EventId: Ulid.NewUlid().ToString(),          // sortable, unique, no central sequencer
            CausedByEventId: ctx?.CurrentEventId,
            CausedByStream: ctx?.SourceStream,
            CorrelationId: ctx?.CorrelationId ?? Ulid.NewUlid().ToString(),
            Timestamp: DateTimeOffset.UtcNow,
            TraceParent: Activity.Current?.Id);

        RaiseEvent(envelope);
        await ConfirmEvents();

        // Push a lightweight pointer to the CausationIndex neuron so forward walks find this event
        // from its parent. The ambient fire synthesizes a system-attributed context.
        if (envelope.CausedByEventId is not null)
        {
            await _contextAccessor.AmbientFire.FireAsync(
                new EventLinked(
                    ParentEventId: envelope.CausedByEventId,
                    ChildEventId: envelope.EventId,
                    ChildStream: this.GetPrimaryKeyString(),
                    Timestamp: envelope.Timestamp),
                correlationId: envelope.CorrelationId,
                ct: ct);
        }
    }

    protected override void TransitionState(TState state, EventEnvelope<TEvent> envelope)
        => Apply(state, envelope.Payload);

    protected abstract void Apply(TState state, TEvent @event);

    // Memory retrieval — strip envelopes and return the typed payloads
    public async Task<IReadOnlyList<TEvent>> GetHistoryAsync(int lastN = 100)
    {
        var envelopes = await RetrieveConfirmedEvents(Math.Max(0, Version - lastN), Version);
        return envelopes.Select(e => e.Payload).ToList();
    }

    public async Task<IReadOnlyList<EventEnvelope<TEvent>>> GetHistoryWithMetadataAsync(int lastN = 100)
    {
        var envelopes = await RetrieveConfirmedEvents(Math.Max(0, Version - lastN), Version);
        return envelopes.ToList();
    }

    // IJournaledNeuronQuery — used by the Playback neuron for backward walks
    public async Task<EventEnvelope<TEvent>?> FindEventAsync(string eventId)
    {
        var envelopes = await RetrieveConfirmedEvents(0, Version);
        return envelopes.FirstOrDefault(e => e.EventId == eventId);
    }
}
```

### 9.1 The `EventEnvelope<T>` wrapper

```csharp
[GenerateSerializer]
public sealed record EventEnvelope<T>(
    [property: Id(0)] T Payload,
    [property: Id(1)] string EventId,              // Ulid, sortable, unique
    [property: Id(2)] string? CausedByEventId,     // null = root event (user intent or ambient)
    [property: Id(3)] string? CausedByStream,      // grain key of the neuron that caused this
    [property: Id(4)] string CorrelationId,
    [property: Id(5)] DateTimeOffset Timestamp,
    [property: Id(6)] string? TraceParent)         // W3C traceparent for OTel correlation
    where T : class, ISynapse;
```

**The framework writes the envelope; authors never see it.** `RaiseAsync(myEvent)` stores `EventEnvelope<MyEvent>`; `GetHistoryAsync` strips it and returns `IReadOnlyList<MyEvent>`. `GetHistoryWithMetadataAsync` is the escape hatch for tooling (Playback, CausationIndex).

### 9.2 Storage backend — Redis via Aspire

`[StorageProvider(ProviderName = "NeuronStore")]` delegates to `Microsoft.Orleans.Persistence.Redis`. The Aspire AppHost wires it:

```csharp
// Ino.AppHost/AppHost.cs
var neuronStore = builder.AddRedis("neuron-store");

var ino = builder.AddIno("ino")
    .WithReference(neuronStore);
```

Silo-side registration (inside the `AddIno` extension):

```csharp
silo.AddRedisGrainStorage("NeuronStore", options =>
{
    options.ConfigurationOptions = ConfigurationOptions.Parse(connectionString);
});
silo.AddLogStorageBasedLogConsistencyProvider("LogStorage");
```

Two lines. That's the full persistence stack for every neuron.

### 9.3 Why LogStorage works for specialized neurons

`LogStorage` persists the entire event list as a single serialized blob per grain. Orleans docs say it's "not suitable for production use unless the event sequences are guaranteed to remain fairly short."

**Specialized neurons satisfy that constraint by design.** The ino architecture pushes authors toward many small neurons rather than one generic neuron per capability:

| Neuron | Events per user per year |
|---|---|
| `GoogleAuthForUberRides` | ~10-30 |
| `GoogleAuthForGmailRead` | ~10-30 |
| `TripPlanner` | ~5-50 |
| `AutoCheckIn.Watcher` | ~5-50 |
| `NotesManager` | ~100-1000 |

LogStorage handles tens of thousands of events per grain before the "rewrite the whole blob on every append" pattern starts hurting. POC-and-early-prod is two orders of magnitude below the cliff.

### 9.4 Migration path for high-volume neurons

If a specific neuron outgrows LogStorage, the migration is per-neuron:

```csharp
// Only for neurons that outgrow LogStorage
public abstract class ChunkedNeuron<TState, TEvent> :
    JournaledGrain<TState, EventEnvelope<TEvent>>,
    ICustomStorageInterface<TState, EventEnvelope<TEvent>>
    where TState : class, new()
    where TEvent : class, ISynapse
{
    // Implements chunked append via Redis sorted sets or a dedicated event store.
    // Public API (RaiseAsync / Apply / GetHistoryAsync) identical to Neuron<TState, TEvent>.
}
```

One neuron class changes base class; every other neuron stays on `Neuron<TState, TEvent>` + LogStorage. No API break.

## 10. Cross-silo dispatch — `ctx.Fire<T>()` runtime

### 10.1 Two silos, same dispatch primitive

Every `ctx.Fire<T>(payload)` call inside a handler goes through the same runtime regardless of whether the target lives in the same silo or not. The runtime picks the fast path vs the cross-silo path based on a runtime discovery lookup.

```csharp
public async Task<NeuronResult> Fire<TSynapse>(
    TSynapse synapse, CancellationToken ct)
    where TSynapse : ISynapse
{
    var synapseTypeName = typeof(TSynapse).FullName!;

    // 1. Discover the canonical target
    var target = await _discovery.LookupCanonicalAsync(synapseTypeName, ct);
    if (target is null)
        return NeuronResult.Fail(new SynapseError("no_canonical_handler",
            $"No installed experience implements INeuron<{typeof(TSynapse).Name}>."));

    // 2. Capability check
    _capabilityEnforcer.AssertCanFire(_currentCaller, typeof(TSynapse));

    // 3. OTel span + timeline causation metadata
    using var activity = _activitySource.StartActivity(
        $"fire {synapseTypeName}", ActivityKind.Producer);
    activity?.SetTag("ino.synapse.type", synapseTypeName);
    activity?.SetTag("ino.target.experience", target.Experience);
    activity?.SetTag("ino.correlation_id", _currentCorrelationId);

    // 4. Dispatch — in-silo fast path OR cross-silo gRPC
    NeuronResult result;
    if (target.Silo == _localSiloId)
    {
        var grain = _grainFactory.GetGrain<INeuron<TSynapse>>(
            grainKey: _currentCorrelationId,
            grainClassNamePrefix: target.GrainType);
        result = await grain.HandleAsync(synapse, _contextForTarget, ct);
    }
    else
    {
        var channel = _channels.GetChannel(target.Silo);
        var client = new NeuronFireClient(channel);
        var response = await client.FireAsync(new FireRequest
        {
            SynapseTypeName = synapseTypeName,
            PayloadBytes = _serializer.Serialize(synapse),
            TargetGrainType = target.GrainType,
            CorrelationId = _currentCorrelationId,
            SourceExperience = _currentCaller,
            UserId = _currentUserId ?? "",
            SessionId = _currentSessionId ?? "",
        }, cancellationToken: ct);
        result = _serializer.DeserializeResult(response.ResultBytes);
    }

    activity?.SetTag("ino.result.success", result.Success);
    return result;
}
```

Same shape for `FireBroadcast<T>` — discovers all reactive targets, groups by target silo, fans out in parallel, batches cross-silo calls into one gRPC per target silo.

### 10.2 Cross-silo gRPC contract

```protobuf
service NeuronFire {
  rpc Fire(FireRequest) returns (FireResponse);
  rpc FireBroadcast(FireBroadcastRequest) returns (FireBroadcastResponse);
}

message FireRequest {
  string synapse_type_name = 1;
  bytes payload_bytes = 2;             // Orleans-serialized ISynapse
  string target_grain_type = 3;
  string correlation_id = 4;
  string source_experience = 5;
  string user_id = 6;
  string session_id = 7;
}

message FireResponse {
  bytes result_bytes = 1;              // Orleans-serialized NeuronResult
}

message FireBroadcastRequest {
  string synapse_type_name = 1;
  bytes payload_bytes = 2;
  repeated string target_grain_types = 3;
  string correlation_id = 4;
  string source_experience = 5;
  string user_id = 6;
  string session_id = 7;
}

message FireBroadcastResponse {
  int32 reached_count = 1;
  int32 failed_count = 2;
  repeated string failed_grain_types = 3;
}
```

Authors never touch protobuf — the typed contract lives at the Orleans layer. The protobuf service is purely a transport.

### 10.3 `IAmbientFire` — for background code

Orleans reminders, startup tasks, and the `Neuron<TState, TEvent>.RaiseAsync` method's `EventLinked` push all need to fire synapses from outside a `HandleAsync` invocation — where there's no `NeuronContext` available. `IAmbientFire` is the escape hatch:

```csharp
public interface IAmbientFire
{
    Task<NeuronResult> FireAsync<T>(
        T synapse,
        string? userId = null,
        string? sessionId = null,
        string? correlationId = null,
        CancellationToken ct = default) where T : ISynapse;

    Task FireBroadcastAsync<T>(
        T synapse,
        string? userId = null,
        string? sessionId = null,
        string? correlationId = null,
        CancellationToken ct = default) where T : ISynapse;
}
```

Same runtime, synthesized context with `SourceExperience = "<ambient>"`. Capability enforcement still applies (granted at the silo level for ambient callers). Timeline and OTel recording unchanged.

### 10.4 The Roslyn analyzer (`Ino.Core.Hosting.Analyzers`)

Shipped as an `AnalyzerReference` transitively pulled in by `Ino.Core.Hosting`. Every experience project gets it automatically. Rules:

| ID | Severity | Description |
|---|---|---|
| `INO001` | Error | Direct `GrainFactory.GetGrain<>()` call targeting a grain from another experience — use `ctx.Fire<T>()` instead. |
| `INO002` | Error | Duplicate `INeuron<T>` implementation in the same assembly. |
| `INO003` | Error | `[UserEntry]` on a type not implementing `ISynapse`. |
| `INO004` | Warning | `ctx.Fire<T>()` called with a synapse type that has no canonical handler in the reference graph. |
| `INO005` | Warning | `ctx.FireBroadcast<T>()` with zero subscribers AND zero canonical handler. |
| `INO006` | Warning | Public payload record not marked `[GenerateSerializer]`. |
| `INO007` | Error | `INeuron<T>`/`IReactsTo<T>` on a non-`sealed` class. |
| `INO008` | Error | `HandleAsync`/`ReactAsync` signature mismatch from the interface. |

The analyzer is the discipline that keeps the architecture from eroding. Without it, a tired author reaches for `GrainFactory.GetGrain<>()` to skip a capability check. With it, the compiler refuses.

## 11. Discovery — runtime, not composed manifest

No `composed-neurons.json` file. No build-time composition step. Discovery is a runtime service.

### 11.1 The `Discovery` grain

Hosted in the `system` silo as a single grain keyed `"global"`.

```csharp
public interface IDiscovery : IGrainWithStringKey
{
    Task RegisterAsync(RegisterRequest request, CancellationToken ct = default);
    Task<CanonicalTarget?> LookupCanonicalAsync(string synapseTypeName, CancellationToken ct = default);
    Task<IReadOnlyList<ReactiveTarget>> LookupReactiveAsync(string synapseTypeName, CancellationToken ct = default);
    Task<IReadOnlyList<RegisteredSilo>> ListSilosAsync(CancellationToken ct = default);
}
```

### 11.2 Registration flow

At silo startup, `Ino.Core.Hosting` reflects over loaded assemblies, finds every `INeuron<T>` / `IReactsTo<T>` implementation, and calls `IDiscovery.RegisterAsync` on the `system` silo via gRPC:

```csharp
var request = new RegisterRequest
{
    SiloId = "experiences",
    Endpoint = "https://localhost:5004",
    Canonical = [
        new("Ino.Travel.TripPlanner.Contracts.PlanTrip", "Ino.Travel.TripPlanner.TripPlanner", "Ino.Travel.TripPlanner"),
        // ...
    ],
    Reactive = [...],
    Capabilities = [...]
};
await _discoveryClient.RegisterAsync(request);
```

### 11.3 Collision detection at registration

`Discovery.RegisterAsync` enforces: for every canonical `(SynapseType, SiloId)` pair, only one registration is accepted. A second registration for the same synapse type fails with a clear error, and the registering silo fails startup with:
```
DiscoveryConflictException:
  Ino.Travel.TripPlanner.TripPlanner (in experiences silo) cannot register
  as the canonical handler for PlanTrip — already registered to
  Ino.Travel.TripPlannerAlt.AltPlanner.
```

Loud startup failure instead of silent runtime conflict. Same check that a build-time composed manifest would have done, just at startup.

### 11.4 Routing cache

Each silo's `Ino.Core.Hosting` maintains an in-memory cache keyed by synapse type. First `ctx.Fire<T>()` for a given type queries `IDiscovery`; subsequent calls use the cache. Cache is cleared on silo restart (typically triggered by install/uninstall via Aspire `ResourceCommandService`), so staleness is bounded by Aspire's restart latency.

### 11.5 Debug endpoint

The `system` silo exposes `GET /discovery/table` that returns the current registry as JSON. Useful for debugging and documenting live state.

## 12. Search — the 3-level hierarchy

Search is implemented in the `system` silo. All three levels go through `ctx.Search.*` facades which fire typed synapses at the corresponding `INeuron<T>` implementations.

| Level | What | When used | Scope |
|---|---|---|---|
| **1. Domain search** | Find installed experiences matching an intent | "what can you do?", first phase of "call a taxi" | Experience metadata (description, keywords, publishes, subscribes) |
| **2. Capability search** | Given candidate experiences, find which synapse type handles this intent | second phase of intent resolution | The experience's `[UserEntry]` schemas |
| **3. Memory search** | Find past synapses (user history, preferences, state) | "what's my home address?", dependency resolution | **The target neuron's own journal** — read via `GetHistoryAsync` on the grain |

### 12.1 Corpus structure

Three indexes maintained by the `SearchIndexer` grain in the `system` silo:

- **Experience index** — one row per installed experience, built from `ExperienceMetadata`. Populated at discovery registration time. Rebuilt on install/uninstall.
- **Capability index** — `(experience_id, synapse_type, is_user_entry)` rows, from the same source.
- **Memory index** — **not a central table.** Memory search is performed by locating the target neuron via level-1+2 and calling its `GetHistoryAsync` method. No central memory store; each neuron's journal is its own memory.

### 12.2 Facades and contracts

```csharp
public interface ISearchFacade
{
    // Level 1 — find installed experiences matching a query
    Task<IReadOnlyList<ExperienceMatch>> DomainsAsync(
        string query, int topK = 5, CancellationToken ct = default);

    // Level 2 — find which typed synapse in these experiences handles this intent
    Task<CapabilityMatch?> CapabilityAsync(
        string query, string[] inExperiences, CancellationToken ct = default);

    // Level 3 — read a specific neuron's memory for a typed match
    Task<MemoryHit<T>?> MemoryAsync<T>(
        string query, CancellationToken ct = default) where T : ISynapse;

    // Full cascade — user intent → (experience, synapse, payload) triple
    Task<ResolvedIntent> ResolveIntentAsync(
        string userIntent, CancellationToken ct = default);

    // Introspection — used by self-improvement, not routing
    Task<IReadOnlyList<NeuronMatch>> NeuronsAsync(
        string query, string[]? inExperiences = null, CancellationToken ct = default);
}
```

Each method backs a typed synapse handler in the `system` silo: `INeuron<DomainsQuery>`, `INeuron<CapabilityQuery>`, `INeuron<MemoryQuery<T>>`, `INeuron<ResolveIntentQuery>`, `INeuron<NeuronsQuery>`.

### 12.3 User intent routing stops at the schema

The search cascade stops when it has identified the canonical synapse type and target experience. From there, `ctx.Fire<T>(payload)` does the actual delivery. **Search never reaches into a specific grain of an experience; it reaches the schema and trusts the experience's declared canonical handler.** This keeps experiences as the encapsulation boundary.

### 12.4 Embedding + reranking

The level-1 and level-2 indexes use an embedding model for ranking. POC default: a local CPU-backed embedding model (`nomic-embed-text-v1.5` or similar, configurable). Reranking on low confidence optionally uses an LLM call via `IChatClient`. Deterministic for the POC; LLM rerank is off by default to keep tests fast.

## 13. Causal memory — Playback, CausationIndex, BranchManager

There is no central timeline. There is no timeline silo. Each neuron's journal is its own memory. Cross-neuron views are reconstructed from the causation metadata stored on every event envelope.

### 13.1 Backward walk — free via envelope pointers

Every `EventEnvelope<T>` carries `CausedByEventId` and `CausedByStream`. To walk backward from any event:

1. Read event B → `(B.CausedByStream, B.CausedByEventId)`
2. Go to neuron at `B.CausedByStream`, call `IJournaledNeuronQuery.FindEventAsync(B.CausedByEventId)` → event A
3. Repeat until `CausedByEventId is null` (root event = user intent or ambient trigger).

No index needed. Each hop is one grain call (local or cross-silo). The `Playback` neuron (section 13.3) exposes this as a typed synapse API.

### 13.2 Forward walk — `CausationIndex` neuron

Forward walk ("what events did event A cause?") can't be reconstructed from envelope pointers alone — you'd have to scan every neuron's journal. The POC uses a dedicated indexing neuron in the `system` silo:

```csharp
namespace Ino.System;

public sealed class CausationIndex :
    Neuron<CausationIndexState, EventLinked>,
    INeuron<EventLinked>,
    INeuron<WalkForwardRequest>
{
    public async Task<NeuronResult> HandleAsync(
        EventLinked link, NeuronContext ctx, CancellationToken ct)
    {
        await RaiseAsync(link, ct);
        return NeuronResult.Ok();
    }

    public Task<NeuronResult> HandleAsync(
        WalkForwardRequest req, NeuronContext ctx, CancellationToken ct)
    {
        var children = State.Children.GetValueOrDefault(req.EventId, []);
        return Task.FromResult(NeuronResult.Ok().With(new ForwardChildren(children)));
    }

    protected override void Apply(CausationIndexState state, EventLinked link)
    {
        if (!state.Children.TryGetValue(link.ParentEventId, out var list))
            state.Children[link.ParentEventId] = list = [];
        list.Add(new ChildRef(link.ChildEventId, link.ChildStream, link.Timestamp));
    }
}

[GenerateSerializer]
public sealed class CausationIndexState
{
    [Id(0)] public Dictionary<string, List<ChildRef>> Children { get; set; } = new();
}
```

The `Neuron<TState, TEvent>` base class automatically fires `EventLinked` at `CausationIndex` on every `RaiseAsync` via `IAmbientFire`. Forward walk is an O(1) lookup in its in-memory map.

### 13.3 Decay on `CausationIndex`

`CausationIndex` IS the one place where bounded growth isn't automatic. Its state grows monotonically with every event in the system. **Track A ships the decay consolidation job for this specific neuron — not deferred.**

An Orleans reminder runs nightly:
- Entries older than 90 days: deleted from `State.Children`.
- Configurable via `IOptions<CausationIndexOptions>` for production tuning.

If `CausationIndex` still becomes the bottleneck at scale, it migrates to `ICustomStorageInterface<CausationIndexState, EventLinked>` + Redis sorted sets (native prefix queries by parent event id). Same public API, different storage. One neuron's implementation changes, nothing else.

### 13.4 The `Playback` neuron

```csharp
namespace Ino.System;

public sealed class Playback :
    Neuron<PlaybackState, PlaybackEvent>,
    INeuron<WalkBackwardRequest>,
    INeuron<WalkForwardRequest>,
    INeuron<CorrelationTraceRequest>
{
    readonly IGrainFactory _grains;

    public async Task<NeuronResult> HandleAsync(
        WalkBackwardRequest req, NeuronContext ctx, CancellationToken ct)
    {
        var chain = new List<EventSnapshot>();
        var cursor = (req.StartEventId, req.StartStream);

        while (cursor is ({} eventId, {} stream) && chain.Count < req.MaxDepth)
        {
            var query = _grains.GetGrain<IJournaledNeuronQuery>(stream);
            var envelope = await query.FindEventAsync(eventId);
            if (envelope is null) break;

            chain.Add(Snapshot(envelope));
            cursor = (envelope.CausedByEventId, envelope.CausedByStream);
        }

        return NeuronResult.Ok().With(new CausalChain(chain));
    }

    public async Task<NeuronResult> HandleAsync(
        WalkForwardRequest req, NeuronContext ctx, CancellationToken ct)
    {
        // Delegate to CausationIndex for one-level lookup, recurse for depth
        var index = _grains.GetGrain<INeuron<WalkForwardRequest>>("global");
        var tree = await RecursivelyExpand(index, req.StartEventId, req.MaxDepth, ct);
        return NeuronResult.Ok().With(new CausalTree(tree));
    }

    public async Task<NeuronResult> HandleAsync(
        CorrelationTraceRequest req, NeuronContext ctx, CancellationToken ct)
    {
        // Walk forward from any event in the correlation, fetching full envelopes
        // from each neuron's journal as we go.
        var events = await CollectCorrelationAsync(req.CorrelationId, ct);
        return NeuronResult.Ok().With(new CorrelationTrace(events));
    }

    protected override void Apply(PlaybackState state, PlaybackEvent _) { /* stateless */ }
}
```

### 13.5 `BranchManager` neuron — time travel

```csharp
public sealed class BranchManager :
    Neuron<BranchManagerState, BranchEvent>,
    INeuron<CreateBranchRequest>,
    INeuron<ListBranchesRequest>,
    INeuron<DeleteBranchRequest>
{
    public async Task<NeuronResult> HandleAsync(
        CreateBranchRequest req, NeuronContext ctx, CancellationToken ct)
    {
        var newBranchId = $"{req.ParentBranch}:fork-{Ulid.NewUlid()}";
        // Branch-scoped grain activations key off NeuronContext.BranchId.
        // The base class writes to a branch-suffixed stream key.
        await RaiseAsync(new BranchCreated(
            BranchId: newBranchId,
            ParentBranch: req.ParentBranch,
            ParentEventId: req.ParentEventId,
            CreatedAt: DateTimeOffset.UtcNow,
            Label: req.Label), ct);
        return NeuronResult.Ok().With(new BranchInfo(newBranchId, req.ParentBranch, req.ParentEventId));
    }
    // ListBranches, DeleteBranch similar
}
```

**Branches are write-once scratch spaces for POC.** Neurons that want to operate in a branch context receive `NeuronContext.BranchId` and the base class keys journals by `(GrainKey, BranchId)`. Main-branch journals are unaffected. Merge semantics are deferred to a later spec — branches are "create, run, diff via Playback, delete" for POC.

## 14. Identity silo — TripRadar pattern + `ExternalGrant`

### 14.1 Pattern lifted from TripRadar

The identity silo's data model is a direct port of the TripRadar shape documented in `D:\TripRadar\src\TripRadar.Server.Domain\Aggregates\User.cs`:

- `User` aggregate — Id, IsActive, TierId, subscription ref, data storage consent, timestamps, `UserProfile` child
- `UserProfile` entity — email, password (BCrypt), Google ID, Telegram user ID, refresh token for ino JWT, security stamp, lockout state, timezone/language/country references
- Password auth, Google OAuth sign-in, Telegram Mini App sign-in — all three already present in TripRadar's `Authentication` orchestrators and brought over unchanged

### 14.2 One new entity — `ExternalGrant`

TripRadar stores "how did this user sign into TripRadar." ino also needs "here's the Uber OAuth token the TripPlanner experience can use." That's a separate concern — external-service credentials granted to specific experiences. One new entity:

```csharp
namespace Ino.Identity.Domain.Entities;

public class ExternalGrant : Entity<long>
{
    public long UserId { get; private set; }
    public string Provider { get; private set; } = null!;       // "google.com", "airline.united", "uber.rides"
    public byte[] AccessTokenEncrypted { get; private set; } = null!;
    public byte[]? RefreshTokenEncrypted { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public string ScopesJson { get; private set; } = null!;     // JSON array of granted scopes
    public string GrantedToExperiencesJson { get; private set; } = "[]";  // experiences with consent to use this
    public DateTime CreatedOn { get; private set; }
    public DateTime? UpdatedOn { get; private set; }
}
```

Encryption key derived from Postgres column encryption or OS key storage (DPAPI / Keychain / libsecret).

### 14.3 The `IIdentityFacade`

```csharp
public interface IIdentityFacade
{
    // Non-throwing lookup — returns null if not authenticated or unauthorized
    Task<IdentityCredential?> GetAsync(string provider, CancellationToken ct = default);

    // Throws IdentityUnavailableException if not producible
    Task<IdentityCredential> RequireAsync(
        string provider, string[] scopes, CancellationToken ct = default);

    // Explicitly revoke for the calling experience (other experiences keep working)
    Task RevokeAsync(string provider, CancellationToken ct = default);
}

[GenerateSerializer]
public sealed record IdentityCredential(
    [property: Id(0)] string Provider,
    [property: Id(1)] string AccessToken,
    [property: Id(2)] DateTimeOffset ExpiresAt,
    [property: Id(3)] IReadOnlyList<string> Scopes,
    [property: Id(4)] string? RefreshToken = null);
```

### 14.4 OAuth flow orchestration

When `RequireAsync` cannot be satisfied from the vault, the identity silo:

1. **Fires `ConsentRequested` synapse** at the `system` silo (relayed to Flutter via gRPC stream, Track D).
2. **Computes the provider's authorization URL** with scopes, `state` CSRF token, and the loopback callback URL.
3. **Fires `BrowserOpenRequested` synapse** → Flutter opens the URL in the user's default browser.
4. **User authenticates**, provider redirects to `http://127.0.0.1:<port>/oauth/callback/{provider}`.
5. **Callback endpoint** validates state, exchanges code for tokens, writes to `ExternalGrant`, adds the calling experience to `GrantedToExperiencesJson`, fires `ConsentGranted`.
6. **Original `RequireAsync`** (awaiting a `TaskCompletionSource` keyed by correlation) resolves with the new credential.

Provider registrations live in `~/.ino/oauth-providers.json`. POC ships registrations for Google + a mock airline provider for test scenarios.

### 14.5 Credential reuse across experiences

When experience B requests the same provider another experience already has:

1. Existing credential covers the requested scopes → consent prompt reads **"TripPlanner already has this; approve for Calendar too?"** → user approves → `GrantedToExperiencesJson` grows by one entry → done, no OAuth flow.
2. Existing credential lacks some scopes → incremental authorization flow requested → provider returns expanded token → `ExternalGrant` updated → B added to grants.
3. Consent denied → B gets `IdentityUnavailableException`; A's credential is unchanged.

### 14.6 Internal grains

The identity silo's logic is implemented as neurons, not as special-case kernel code. Grains include:
- `INeuron<RequireIdentityRequest>` — the canonical handler for `ctx.Identity.RequireAsync`
- `INeuron<GetIdentityRequest>` — for `GetAsync`
- `IReactsTo<ConsentGranted>` — updates grants table
- `IReactsTo<ConsentDenied>` — fails pending requires
- `INeuron<RevokeRequest>` — removes grants

The identity silo uses the same primitive contract as every experience. No special access, no kernel-only API.

## 15. Marketplace POC

### 15.1 Endpoints

Six endpoints, hosted by the `system` silo's ASP.NET HTTP server:

```
GET   /marketplace/available              — list available experiences (JSON)
GET   /marketplace/available/{id}         — one experience's full metadata
GET   /marketplace/installed              — currently installed experiences
POST  /marketplace/install/{id}           — start install (returns 202 + consent token)
POST  /marketplace/install/{id}/consent   — approve consent and run BDD gate
POST  /marketplace/uninstall/{id}         — uninstall + silo restart
GET   /discovery/table                    — debug dump of the discovery registry
```

### 15.2 The "available" feed (POC)

A JSON file at `~/.ino/marketplace.json` containing metadata for every experience the POC knows about:

```json
{
  "experiences": [
    {
      "id": "Ino.Notes",
      "description": "Simple notes — create, list, delete.",
      "version": "1.0.0",
      "publisher": "ino",
      "requires": []
    },
    {
      "id": "Ino.Travel",
      "description": "Plan trips, search flights and hotels, discover places, automatic check-in.",
      "version": "1.0.0",
      "publisher": "ino",
      "requires": ["Http:serpapi.com", "Http:*.airlines", "Identity:airline.*", "Llm:Reasoning"]
    }
  ]
}
```

Pre-populated with every bundle in the POC solution. Track G replaces this with a remote feed — schema unchanged.

### 15.3 How "install" works without dynamic assembly loading

The POC cheats: **every experience in the solution is already compiled into the AppHost as an unconditional dependency**. What "installed" means is whether it's wired into the `AddExperiences<T>()` call list. The AppHost reads `~/.ino/installed.json`:

```json
{ "installed": ["Ino.Notes", "Ino.Travel"] }
```

and conditionally wires experiences:

```csharp
var installed = InstalledSet.Load();
if (installed.Contains("Ino.Notes"))    ino.AddExperiences<Notes>();
if (installed.Contains("Ino.Travel"))   ino.AddExperiences<Travel>();
```

`POST /marketplace/install/{id}` flow:
1. Run the experience's BDD suite via `Ino.Testing.InoTestHost` with its bundled `mocks/llm.recordings.yml`.
2. If green: append the id to `installed.json`, trigger Aspire `ResourceCommandService.ExecuteCommand("experiences", "rebuild")` to restart the experiences silo.
3. If red: HTTP 400 with the failing scenario name + unmatched prompts. No state change.

Track G replaces "append to JSON + restart" with "download NuGet + `AssemblyLoadContext` hot-load + restart." The API contract doesn't change.

### 15.4 Two-step consent

```
POST /marketplace/install/Ino.Travel
  → 202 Accepted
    {
      "status": "awaiting_consent",
      "experience_id": "Ino.Travel",
      "capabilities": [
        { "kind": "Http",       "config": "serpapi.com",     "description": "Flight/hotel search API" },
        { "kind": "Http",       "config": "*.airlines",      "description": "Airline check-in APIs" },
        { "kind": "Identity",   "config": "airline.*",       "description": "Store airline credentials" },
        { "kind": "Llm",        "config": "Reasoning",       "description": "Synthesize trip itineraries" }
      ],
      "consent_token": "<opaque>"
    }

POST /marketplace/install/Ino.Travel/consent
  Body: { "token": "<opaque>", "approved_capabilities": ["Http:serpapi.com", "Http:*.airlines", "Identity:airline.*", "Llm:Reasoning"] }
  → 200 OK
    { "status": "installed", "installed_experiences": [...] }
  OR
  → 400 Bad Request
    { "status": "bdd_failure", "failing_scenario": "...", "details": "..." }
```

The Flutter client drives this flow. Authors or scripted tests can drive it via direct HTTP for validation. No CLI involved.

## 16. Telemetry contract

Every silo configures OpenTelemetry at startup. Every facade call emits one span automatically. Every grain method gets tracing via Orleans' OTel integration.

### 16.1 Spans (ActivitySource `ino`)

| Span | Kind | Attributes |
|---|---|---|
| `fire {SynapseType}` | Producer | `ino.synapse.type`, `ino.source.experience`, `ino.target.experience`, `ino.target.grain_type`, `ino.correlation_id`, `ino.user_id`, `ino.result.success`, `ino.error.code` |
| `broadcast {SynapseType}` | Producer | `ino.synapse.type`, `ino.source.experience`, `ino.listener_count`, `ino.correlation_id` |
| `handle {SynapseType}` | Consumer | `ino.synapse.type`, `ino.target.experience`, `ino.correlation_id`, `ino.result.success` |
| `react {SynapseType}` | Consumer | same as handle |
| `search.{domains\|capability\|memory\|neurons\|resolve}` | Internal | `ino.search.level`, `ino.search.hit_count`, `ino.search.confidence` |
| `identity.{get\|require\|revoke}` | Internal | `ino.identity.provider`, `ino.identity.scopes`, `ino.identity.cached` |
| `raise {EventType}` | Internal | `ino.event.type`, `ino.grain.key`, `ino.event_id`, `ino.caused_by_event_id` |

Parent-child via W3C traceparent propagated across grain calls, cross-silo gRPC, and `IAmbientFire` synthesized contexts.

### 16.2 Metrics (Meter `ino`)

| Metric | Type | Tags |
|---|---|---|
| `ino.synapse.fires` | Counter | `synapse_type`, `source_experience`, `target_experience`, `success` |
| `ino.synapse.duration` | Histogram (ms) | `synapse_type`, `source_experience`, `target_experience` |
| `ino.synapse.broadcasts` | Counter | `synapse_type`, `source_experience`, `listener_count` |
| `ino.search.queries` | Counter | `level`, `success` |
| `ino.identity.grants` | Counter | `provider`, `result` |
| `ino.experiences.installed` | Gauge | `experience_id` |

### 16.3 Logs

`ILogger<T>` flows through the OTel logs exporter. Every log emitted via `ctx.Logger` is auto-decorated with `ino.synapse.type`, `ino.correlation_id`, `ino.source.experience`, `ino.user_id`.

### 16.4 Three-layer reservation (deferred)

| Layer | Purpose | Track A |
|---|---|---|
| **1. Local diagnostics** | OTel → Aspire dashboard | **Ships** |
| **2. Anonymized improvement telemetry** | OTel → platform aggregator with consent gate | Reserved (same OTel shape, new exporter) |
| **3. Experience author feedback** | Aggregated platform data, filtered by experience | Reserved |

The contract is one OTel shape. Layers 2 and 3 add exporters and consent plumbing downstream of layer 1 — no new instrumentation ever required.

## 17. Test strategy — five layers

| Layer | Project | Scope | Speed target |
|---|---|---|---|
| **L1** unit | `tests/Ino.Core.Tests` | `ISynapse`, `NeuronResult`, `EventEnvelope<T>`, capability matching — no Orleans | <5s full suite |
| **L2** silo integration | `tests/Ino.System.Tests`, `tests/Ino.Identity.Tests`, `tests/Ino.Hosting.Tests` | Each kernel silo via **shared** `InoTestSiloFixture` with `ICollectionFixture<T>` | <30s per project |
| **L3** experience BDD | `experiences/<cluster>/<experience>/Ino.*.Tests` | One `.Tests` project per experience, Reqnroll, `RecordedMockChatClient`, shared cluster fixture | <15s per project |
| **L4** cross-experience BDD | `tests/Ino.Bdd` | Multi-experience scenarios with N experiences loaded into one shared fixture | <60s |
| **L5** full AppHost E2E | `tests/Ino.E2E` | `DistributedApplicationTestingBuilder` + real Postgres + real Redis + Playwright (stubbed in Track A) + the 10 canonical scenarios | 3-5 minutes |

**Total suite target: <10 minutes from clean.**

### 17.1 Shared `InoTestSiloFixture` — the bloat fix

The existing `D:\ino\tests\Core.Tests\` spends ~100s of its 2-minute runtime on TestCluster startup because each test class creates its own cluster. The POC explicitly avoids this:

```csharp
public sealed class InoTestSiloFixture : IAsyncLifetime
{
    public TestCluster Cluster { get; private set; } = null!;
    public IGrainFactory Grains => Cluster.Client;
    public InoTestLlm Llm { get; } = new();
    public InoTestIdentity Identity { get; } = new();
    public FakeTimeProvider Clock { get; } = new();

    public async ValueTask InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<TestSiloConfigurator>();
        Cluster = builder.Build();
        await Cluster.DeployAsync();
    }

    public ValueTask DisposeAsync() => new(Cluster.DisposeAsync().AsTask());

    public Task ResetAsync()
    {
        // Wipes grain state, mock LLM recordings, identity vault, clock
        // Called between tests — fast because everything is in-memory
        return Task.CompletedTask; // implementation details elided
    }

    class TestSiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder silo)
        {
            silo.AddMemoryGrainStorage("NeuronStore")             // in-memory for L1-L4
                .AddLogStorageBasedLogConsistencyProvider("LogStorage")
                .ConfigureServices(services =>
                {
                    services.AddSingleton<IChatClient>(/* inject test LLM */);
                    services.AddSingleton<IIdentityVault>(/* inject stub */);
                    services.AddSingleton<TimeProvider>(/* inject fake clock */);
                });
        }
    }
}

[CollectionDefinition(nameof(InoTestCollection))]
public sealed class InoTestCollection : ICollectionFixture<InoTestSiloFixture> { }
```

**One `TestCluster` per test project**, shared via xunit.v3's `ICollectionFixture<T>`. Cluster startup cost (~5-10s) paid once per project. Between tests, `ResetAsync()` wipes in-memory state in ~50ms.

### 17.2 `RecordedMockChatClient` — deterministic LLM

```csharp
public sealed class RecordedMockChatClient : IChatClient
{
    readonly IReadOnlyList<LlmRecording> _recordings;
    readonly List<string> _unmatched = [];

    public async Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken ct = default)
    {
        var lastMessage = request.Messages.LastOrDefault(m => m.Role == "user")?.Content ?? "";
        var match = _recordings.FirstOrDefault(r => Regex.IsMatch(lastMessage, r.MatchPattern));

        if (match is null)
        {
            _unmatched.Add(lastMessage);
            throw new MockLlmMissException(
                $"No recorded response matched:\n{lastMessage}\n\n" +
                $"Add a recording to mocks/llm.recordings.yml.");
        }

        return match.ToChatResponse();
    }

    public IReadOnlyList<string> UnmatchedPrompts => _unmatched;
}
```

- **Regex matching** on the last user message
- **Missing recording = loud test failure** with a suggested recording template
- **`UnmatchedPrompts` tracked** so tests can assert `Should().BeEmpty()` at the end

Recording format:

```yaml
- match: "synthesize.*itinerary.*Tokyo.*5 days"
  json:
    summary: "5 days in Tokyo: Shibuya, Asakusa, Akihabara, Hakone, Ginza."
    days: [ ... ]

- match: "confirm.*ride"
  text: "Confirmed. Your ride to LAX is booked for 3:00 PM."

- match: "resolve airport code for Tokyo"
  text: "NRT"
```

Authors record mocks once via `RecordingChatClient` (a wrapper around a real LLM), commit the YAML, tests replay deterministically. Install-time gate (section 15.3) uses the same mocks file.

### 17.3 L5 E2E with real containers

`DistributedApplicationTestingBuilder` from Aspire's testing kit spins up a real AppHost with Postgres + Redis containers:

```csharp
public sealed class InoE2EFixture : IAsyncLifetime
{
    DistributedApplication _app = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Ino_AppHost>();
        _app = await builder.BuildAsync();
        await _app.StartAsync();
        await _app.ResourceNotifications.WaitForResourceHealthyAsync("system");
        await _app.ResourceNotifications.WaitForResourceHealthyAsync("identity");
        await _app.ResourceNotifications.WaitForResourceHealthyAsync("experiences");
    }

    public HttpClient CreateSystemClient() => _app.CreateHttpClient("system");
    public ValueTask DisposeAsync() => new(_app.DisposeAsync().AsTask());
}
```

**Virtual time for reminder-driven scenarios** via `FakeTimeProvider` injected into Orleans (Orleans 8+ respects `TimeProvider`). The E2E fixture swaps in `FakeTimeProvider`; tests advance time manually instead of waiting 24 hours for scenario 7.

## 18. Flutter stub pattern

Track A preserves the E2E fixture shape from `D:\ino\tests\E2E.Tests\Infrastructure\NeuronE2ETest.cs` — dual Kestrel endpoint, same-origin gRPC-Web + static files, Playwright Chromium interception — but Flutter-specific code is stubbed out until Track D wires the Flutter client.

### 18.1 Partial class split, file excluded from `.csproj`

```
POC/tests/Ino.E2E/Infrastructure/
├── NeuronE2ETest.cs                 # base class + server-side helpers (compiled)
├── GrpcTestFixture.cs               # InoTestHost + HTTP/2 gRPC endpoint (compiled)
├── GrpcTestFixture.Flutter.cs       # Playwright + browser endpoint + static files (EXCLUDED)
├── NeuronE2ETest.Flutter.cs         # OpenBrowserAndVerify + assertion helpers (EXCLUDED)
└── README.md                        # re-enablement recipe
```

`.csproj`:
```xml
<ItemGroup>
  <!-- Flutter browser verification deferred to Track D. Re-enable via README.md recipe. -->
  <Compile Remove="Infrastructure/GrpcTestFixture.Flutter.cs" />
  <Compile Remove="Infrastructure/NeuronE2ETest.Flutter.cs" />
</ItemGroup>

<!-- <ItemGroup>
  <PackageReference Include="Microsoft.Playwright" Version="1.49.0" />
</ItemGroup>

<PropertyGroup>
  <DefineConstants>$(DefineConstants);FLUTTER_ENABLED</DefineConstants>
</PropertyGroup> -->
```

### 18.2 Partial method hooks

`GrpcTestFixture.cs` (compiled) declares `partial` method hooks the Flutter file (excluded) implements:

```csharp
// Compiled — main file
public sealed partial class GrpcTestFixture : IAsyncLifetime
{
    public async ValueTask InitializeAsync()
    {
        // ... start InoTestHost + HTTP/2 gRPC endpoint ...
        await InitializeBrowserAsync();    // no-op when Flutter file isn't compiled
    }

    partial Task InitializeBrowserAsync();
    partial Task DisposeBrowserAsync();
    static partial void ConfigureKestrelForBrowser(KestrelServerOptions k);
    static partial void ConfigureAppForBrowser(WebApplication app);
}
```

### 18.3 Scenario bodies use `#if FLUTTER_ENABLED`

Every scenario is authored with both server-side assertions (always run) and Flutter browser-side assertions behind a preprocessor guard:

```csharp
public sealed class Scenario04_PlanTokyoTrip : NeuronE2ETest
{
    [Fact]
    public async Task PlanTokyoTrip_ReturnsItineraryAndBroadcastsTripPlanned()
    {
        // Arrange
        MockLlm.LoadRecordings("mocks/scenario-04.llm.recordings.yml");
        await Fixture.Host.InstallExperiencesAsync("Ino.Notes", "Ino.Travel");

        // Act (server-side — always runs)
        var response = await ChatAsync("plan a 5-day trip to Tokyo starting next Monday");

        // Assert (server-side)
        Assert.True(response.Success);
        AssertRfwContains(response, "Tokyo");
        var trace = await Fixture.Host.InvokePlaybackAsync(response.CorrelationId);
        Assert.Equal(
            new[] { "UserIntent", "PlanTrip", "SearchFlights", "FlightsFound",
                    "SearchHotels", "HotelsFound", "DiscoverPlaces", "PlacesFound", "TripPlanned" },
            trace.Select(e => e.SynapseType));

#if FLUTTER_ENABLED
        // Assert (browser-side) — runs when Flutter partials are compiled
        var (page, grpcBody) = await OpenBrowserAndVerify(
            "plan a 5-day trip to Tokyo starting next Monday");
        AssertGrpcResponseContains(grpcBody, "Tokyo");
        AssertGrpcResponseContains(grpcBody, "FlightLeg");
        await TakeScreenshot(page, nameof(PlanTokyoTrip_ReturnsItineraryAndBroadcastsTripPlanned));
#endif
    }
}
```

Re-enablement is a 4-step recipe (delete `<Compile Remove>` lines, uncomment `Microsoft.Playwright`, uncomment `FLUTTER_ENABLED` symbol, build Flutter wwwroot) — documented in `POC/tests/Ino.E2E/Infrastructure/README.md`.

## 19. Example — `Ino.Travel.TripPlanner`

A full worked example of what an author writes for a logic-heavy experience. Demonstrates parallel `ctx.Fire`, memory search, LLM usage, broadcast emission, RFW rendering, error handling, and cross-experience contract references.

**`experiences/travel/trip-planner/Ino.Travel.TripPlanner.Contracts/Schemas.cs`:**

```csharp
using Ino.Core;

namespace Ino.Travel.TripPlanner.Contracts;

[UserEntry]
[GenerateSerializer]
public sealed record PlanTrip(
    [property: Id(0)] string DestinationText,
    [property: Id(1)] int DurationDays,
    [property: Id(2)] DateTimeOffset? StartDate,
    [property: Id(3)] decimal? BudgetUsd = null,
    [property: Id(4)] string? Notes = null) : ISynapse;

[GenerateSerializer]
public sealed record TripPlanned(
    [property: Id(0)] string TripId,
    [property: Id(1)] string DestinationText,
    [property: Id(2)] DateTimeOffset StartDate,
    [property: Id(3)] DateTimeOffset EndDate,
    [property: Id(4)] IReadOnlyList<FlightLeg> Flights,
    [property: Id(5)] IReadOnlyList<HotelBooking> Hotels,
    [property: Id(6)] IReadOnlyList<DailyItinerary> Days,
    [property: Id(7)] decimal TotalCostUsd,
    [property: Id(8)] string Summary) : ISynapse;

[GenerateSerializer]
public sealed record TravelPreferences(
    [property: Id(0)] string? PreferredAirline,
    [property: Id(1)] SeatClass PreferredSeatClass,
    [property: Id(2)] HotelClass PreferredHotelClass,
    [property: Id(3)] decimal? DefaultBudgetUsd,
    [property: Id(4)] IReadOnlyList<string> InterestTags) : ISynapse;
```

**`experiences/travel/trip-planner/Ino.Travel.TripPlanner/TripPlanner.cs`:**

```csharp
using Ino.Core;
using Ino.Core.Hosting;
using Ino.Travel.TripPlanner.Contracts;
using Ino.Travel.FlightSearch.Contracts;
using Ino.Travel.HotelSearch.Contracts;
using Ino.Travel.PlaceDiscovery.Contracts;

namespace Ino.Travel.TripPlanner;

[RequiresCapability(typeof(Capability.Llm), LlmTier.Reasoning)]
[RequiresCapability(typeof(Capability.Persistence), "trip-planner")]
public sealed class TripPlanner :
    Neuron<TripPlannerState, TripPlannerEvent>,
    INeuron<PlanTrip>
{
    readonly IChatClient _chat;

    public TripPlanner(IChatClient chat) { _chat = chat; }

    public async Task<NeuronResult> HandleAsync(
        PlanTrip req, NeuronContext ctx, CancellationToken ct)
    {
        // 1. Pull user preferences from memory (or use defaults)
        var prefs = (await ctx.Search.MemoryAsync<TravelPreferences>(
                "user's travel preferences", ct))?.Value ?? TravelPreferences.Default;

        // 2. Resolve airport codes via LLM (deterministic in tests via recorded mocks)
        var origin = await ResolveOriginAirport(ctx, ct);
        var destination = await ResolveAirportCode(req.DestinationText, ctx, ct);
        if (origin is null || destination is null)
            return NeuronResult.Fail(new SynapseError("trip_planner_unresolved_airport",
                $"Could not resolve airport for {req.DestinationText}"));

        var startDate = req.StartDate ?? NextAvailableStart();
        var endDate = startDate.AddDays(req.DurationDays);

        // 3. Parallel fan-out to specialists
        var (flights, hotels, places) = await ParallelSpecialistCalls(
            ctx, origin, destination, startDate, endDate, prefs, ct);

        // 4. Synthesize itinerary (LLM call)
        var itinerary = await SynthesizeItinerary(req, prefs, flights, hotels, places, ct);

        // 5. Persist the trip in this neuron's own journal
        var trip = new TripPlanned(
            TripId: Ulid.NewUlid().ToString(),
            DestinationText: req.DestinationText,
            StartDate: startDate,
            EndDate: endDate,
            Flights: flights?.Results.Take(1).Select(ToFlightLeg).ToList() ?? [],
            Hotels: hotels?.Results.Take(1).Select(h => ToHotelBooking(h, req)).ToList() ?? [],
            Days: itinerary.Days,
            TotalCostUsd: ComputeTotal(flights, hotels, req.DurationDays),
            Summary: itinerary.Summary);

        await RaiseAsync(new TripPlannerEvent.TripBooked(trip), ct);

        // 6. Broadcast — AutoCheckIn, Calendar, and any listener experiences react
        await ctx.FireBroadcast(trip, ct);

        return NeuronResult.Ok(trip.Summary)
            .With(trip)
            .WithRfw(TripRfwTemplate.Build(trip));
    }

    protected override void Apply(TripPlannerState state, TripPlannerEvent @event)
    {
        switch (@event)
        {
            case TripPlannerEvent.TripBooked b:
                state.Trips[b.Trip.TripId] = b.Trip;
                break;
        }
    }

    async Task<(FlightsFound?, HotelsFound?, PlacesFound?)> ParallelSpecialistCalls(
        NeuronContext ctx, string origin, string destination,
        DateTimeOffset startDate, DateTimeOffset endDate,
        TravelPreferences prefs, CancellationToken ct)
    {
        var flightsTask = ctx.Fire(new SearchFlights(origin, destination, startDate, endDate,
            Passengers: 1, Class: prefs.PreferredSeatClass), ct);
        var hotelsTask = ctx.Fire(new SearchHotels(destination, startDate, endDate,
            Guests: 1, Class: prefs.PreferredHotelClass), ct);
        var placesTask = ctx.Fire(new DiscoverPlaces(destination,
            Interests: prefs.InterestTags, Limit: 10), ct);

        await Task.WhenAll(flightsTask, hotelsTask, placesTask);

        return (
            flightsTask.Result.TryGetPayload<FlightsFound>(out var f) ? f : null,
            hotelsTask.Result.TryGetPayload<HotelsFound>(out var h) ? h : null,
            placesTask.Result.TryGetPayload<PlacesFound>(out var p) ? p : null);
    }

    // ResolveAirportCode, ResolveOriginAirport, NextAvailableStart, ToFlightLeg,
    // ToHotelBooking, ComputeTotal, SynthesizeItinerary elided for brevity
}
```

The full travel cluster has five experiences (FlightSearch, HotelSearch, PlaceDiscovery, TripPlanner, AutoCheckIn), each following the same pattern. `AutoCheckIn.Watcher` demonstrates the proactive pattern with Orleans reminders + `IAmbientFire` for firing from background callbacks.

## 20. Example — `Ino.Travel.AutoCheckIn`

The proactive-neuron case. `Watcher` reacts to `TripPlanned` broadcasts, schedules an Orleans reminder for 24 hours before departure, and when the reminder fires uses `IAmbientFire` to fire `PerformCheckIn` at `CheckInAgent`.

```csharp
[RequiresCapability(typeof(Capability.Persistence), "auto-check-in")]
public sealed class Watcher :
    Neuron<WatcherState, WatcherEvent>,
    IReactsTo<TripPlanned>,
    IRemindable
{
    readonly IAmbientFire _ambient;

    public Watcher(IAmbientFire ambient) { _ambient = ambient; }

    // Reactive path — schedule reminders
    public async Task ReactAsync(TripPlanned trip, NeuronContext ctx, CancellationToken ct)
    {
        foreach (var leg in trip.Flights)
        {
            var fireAt = leg.Departure.AddHours(-24);
            if (fireAt <= DateTimeOffset.UtcNow) continue;

            await RaiseAsync(new WatcherEvent.CheckInScheduled(
                TripId: trip.TripId, Leg: leg, FireAt: fireAt), ct);

            await this.RegisterOrUpdateReminder(
                reminderName: $"checkin:{leg.Carrier}:{leg.Departure:O}",
                dueTime: fireAt - DateTimeOffset.UtcNow,
                period: TimeSpan.FromDays(365));
        }
    }

    // Proactive path — reminder callback, no NeuronContext available
    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        var key = reminderName.Replace("checkin:", "");
        if (!State.Pending.TryGetValue(key, out var pending)) return;

        // IAmbientFire — synthesized context with source "<ambient>"
        var result = await _ambient.FireAsync(
            new PerformCheckIn(pending.TripId, pending.Leg, pending.UserId),
            userId: pending.UserId,
            sessionId: pending.SessionId,
            correlationId: $"autocheckin:{pending.TripId}");

        if (result.Success)
        {
            await RaiseAsync(new WatcherEvent.CheckInCompleted(key), CancellationToken.None);
            await this.UnregisterReminder(await this.GetReminder(reminderName));
        }
        else if (pending.Attempts < 3)
        {
            await RaiseAsync(new WatcherEvent.CheckInRetryScheduled(key, pending.Attempts + 1),
                CancellationToken.None);
            await this.RegisterOrUpdateReminder(reminderName,
                dueTime: TimeSpan.FromMinutes(30),
                period: TimeSpan.FromDays(365));
        }
        else
        {
            await _ambient.FireBroadcastAsync(
                new CheckInFailed(pending.TripId, pending.Leg,
                    "Exceeded retry budget", WillRetry: false),
                userId: pending.UserId);
            await this.UnregisterReminder(await this.GetReminder(reminderName));
        }
    }

    protected override void Apply(WatcherState state, WatcherEvent @event)
    {
        switch (@event)
        {
            case WatcherEvent.CheckInScheduled s:
                state.Pending[$"{s.Leg.Carrier}:{s.Leg.Departure:O}"] =
                    new PendingCheckIn(s.TripId, s.Leg, s.UserId ?? "", "", 0);
                break;
            case WatcherEvent.CheckInCompleted c:
                state.Pending.Remove(c.Key);
                break;
            case WatcherEvent.CheckInRetryScheduled r:
                if (state.Pending.TryGetValue(r.Key, out var p))
                    state.Pending[r.Key] = p with { Attempts = r.Attempts };
                break;
        }
    }
}
```

**Key pattern:** proactive neurons mix `IReactsTo<T>` (for scheduling on incoming events) + `IRemindable` (for time-based execution) + `IAmbientFire` (for firing synapses from non-handler contexts). All three primitives ship in Track A; everything else is the author's business logic.

## 21. The 10 canonical AI-native OS scenarios (Track A-bis)

**Track A is done when all 10 run green against the POC AppHost.** Each scenario tests a distinct architectural primitive; several redundantly cover high-value paths. Half are deliberately cross-experience.

### Scenario 1 — First-run boot and self-introspection

```gherkin
Given a fresh ino install with only the Notes and Travel bundles in the AppHost composition
  And no experiences have been installed yet
When the AppHost starts
Then the system silo is healthy
  And the identity silo is healthy
  And the experiences silo is healthy
  And a GET /marketplace/available returns ["Ino.Notes", "Ino.Travel"]
  And a GET /marketplace/installed returns []

When the user sends a chat "what can you do?" to the system silo
Then the response says ino currently has no experiences installed
  And the response suggests visiting the marketplace
```

**Proves:** kernel silos boot cleanly; marketplace endpoints work; level-1 search handles empty state.

### Scenario 2 — Install an experience through the marketplace

```gherkin
Given a fresh ino with no installed experiences
When the user POSTs /marketplace/install/Ino.Notes
Then the response is 202 Accepted with no required capabilities
  And a consent token is returned

When the user POSTs /marketplace/install/Ino.Notes/consent with the token
Then the Notes experience's BDD suite runs in-process
  And every scenario passes
  And RecordedMockChatClient.UnmatchedPrompts is empty
  And the installed.json file has "Ino.Notes" appended
  And Aspire restarts the experiences silo
  And GET /marketplace/installed returns ["Ino.Notes"]

When the user sends chat "what can you do?"
Then the response describes Notes as a capability
```

**Proves:** marketplace install flow end-to-end; two-step consent; BDD gate; silo restart integration.

### Scenario 3 — Create notes and find them via memory search

```gherkin
Given the Notes experience is installed
When the user sends chat "remember that my home address is 123 Main St"
Then system silo classifies the intent and fires CreateNote at the Notes experience
  And the NotesManager grain raises a NoteCreated event in its journal
  And the neuron's state contains one note

When the user sends chat "remember my office is at 500 Elm Ave"
Then a second NoteCreated event is raised
  And the neuron's state contains two notes

When the user sends chat "what's my home address?"
Then system silo fires a MemoryQuery<NoteCreated>
  And the search finds the matching note in NotesManager's journal
  And the response says "123 Main St"
```

**Proves:** neuron journal = memory; level-3 search reads `GetHistoryAsync`; `ctx.Search.MemoryAsync<T>` against JournaledGrain state.

### Scenario 4 — Plan a Tokyo trip end-to-end

```gherkin
Given the Travel bundle is installed
  And the user has no prior trips in any journal
When the user sends chat "plan a 5-day trip to Tokyo starting next Monday"
Then system silo's intent classifier produces a PlanTrip synapse
  And system silo fires PlanTrip at TripPlanner
  And TripPlanner fires SearchFlights, SearchHotels, and DiscoverPlaces in parallel
  And each specialist returns a typed result
  And TripPlanner synthesizes an itinerary via the mocked LLM
  And TripPlanner broadcasts TripPlanned
  And the broadcast reaches AutoCheckIn.Watcher

  And the response carries an RFW description with 5 DailyItinerary entries
  And WalkBackwardRequest on the final NeuronResult finds the chain:
      UserIntent → PlanTrip → SearchFlights → FlightsFound → TripPlanned
  And every event in the chain shares the same correlation_id
```

**Proves:** cross-experience fan-out; typed synapse routing; parallel `ctx.Fire<T>`; broadcast; RFW; causation propagation; correlation stitching.

### Scenario 5 — Preference-aware planning reuses stored memory

```gherkin
Given scenario 4 has completed
  And the user has previously expressed preference for business class and luxury hotels
  And a TravelPreferences event is persisted in a PreferenceLearner neuron's journal

When the user sends chat "plan a 3-day trip to Paris next month"
Then TripPlanner calls ctx.Search.MemoryAsync<TravelPreferences>
  And the search returns the stored preferences
  And TripPlanner passes SeatClass.Business to SearchFlights
  And TripPlanner passes HotelClass.Luxury to SearchHotels
  And the resulting RFW cards reflect the preferences
```

**Proves:** memory search across neurons; preference-aware behavior adaptation.

### Scenario 6 — Google identity granted once, reused across experiences

```gherkin
Given a fresh install
  And no google.com credential exists in the identity vault
When the user installs Ino.GmailFlightConfirmationReader
Then the consent screen lists Identity("google.com", "email", "profile")
  And a BrowserOpenRequested synapse fires with a Google OAuth URL
When the user completes Google OAuth
Then the identity silo stores an encrypted ExternalGrant for google.com
  And the install completes

When the user installs Ino.CalendarReader which also needs google.com email scope
Then the consent screen says "Google access already granted to GmailFlightConfirmationReader"
  And the screen asks to share the existing grant with CalendarReader
When the user approves
Then no new OAuth flow is triggered
  And the ExternalGrant grows by one grant entry
  And both experiences can call ctx.Identity.GetAsync("google.com")
```

**Proves:** identity vault; scoped grants; credential reuse; consent via synapses; OAuth loopback callback.

### Scenario 7 — Automatic check-in 24 hours before departure

```gherkin
Given scenario 4's TripPlanned event exists with a flight departing 2026-05-01T15:00:00Z
  And AutoCheckIn.Watcher scheduled an Orleans reminder for 2026-04-30T15:00:00Z
  And the user has authenticated with "airline.united"

When the virtual clock advances to 2026-04-30T15:00:00Z
Then the Orleans reminder fires in Watcher
  And Watcher fires PerformCheckIn via IAmbientFire with source "<ambient>"
  And CheckInAgent handles PerformCheckIn
  And CheckInAgent pulls the airline credential from the identity silo
  And CheckInAgent calls the mocked airline API and receives seat 14A
  And CheckInAgent broadcasts CheckInCompleted

  And a CorrelationTraceRequest for "autocheckin:<trip-id>" returns:
      UserIntent (original trip) → PlanTrip → TripPlanned → CheckInScheduled
      → (24h later) → PerformCheckIn → CheckInCompleted
  And an OTel span "handle PerformCheckIn" has ino.source.experience = "<ambient>"
```

**Proves:** proactive neurons; Orleans reminders; `IAmbientFire` context synthesis; causation chain across a 24-hour virtual gap.

### Scenario 8 — Playback the causal chain of a completed trip

```gherkin
Given scenarios 4 and 7 have run and all events are journaled
When the user sends chat "show me everything that happened for my Tokyo trip"
Then system silo fires a CorrelationTraceRequest at the Playback neuron
  And Playback queries CausationIndex for events in the correlation
  And Playback walks the graph, fetching events from each source neuron's journal
  And Playback returns 12 events in chronological order

  And the list includes UserIntent, PlanTrip, SearchFlights, FlightsFound, SearchHotels,
      HotelsFound, DiscoverPlaces, PlacesFound, TripPlanned, CheckInScheduled,
      PerformCheckIn, CheckInCompleted
  And every event has a caused_by pointer to its predecessor
  And the response to the user describes the chain in natural language
```

**Proves:** Playback + CausationIndex neurons; causation envelope propagation; backward + forward walks; cross-neuron history reconstruction without a central log.

### Scenario 9 — Time-travel branch for "what if I'd gone to Paris?"

```gherkin
Given scenario 4's main-branch TripPlanned for Tokyo exists
When the user sends chat "what if I had gone to Paris instead?"
Then system silo fires CreateBranchRequest at BranchManager
  And BranchManager creates a branch "what-if-paris-<timestamp>"
  And subsequent fires in the session carry branch_id of the new branch

When the user sends chat "plan a 3-day trip to Paris in the branch"
Then PlanTrip is fired in the branch context
  And TripPlanner's grain activation is branch-scoped
  And a new TripPlanned event is raised in the branch, not in main
  And the main-branch Tokyo trip is unchanged

When the user asks "show me both trips"
Then Playback queries both branches and returns the diff
```

**Proves:** BranchManager; branch-scoped grain activation; branch-local journals; the time-travel story.

### Scenario 10 — Install-time BDD rejection for a broken experience

```gherkin
Given a test-only "Ino.BrokenExperience" bundle is in the POC composition
  And BrokenExperience ships a feature file with a deliberately failing scenario
When the user POSTs /marketplace/install/Ino.BrokenExperience
Then the consent step completes
  And the marketplace runs the BDD suite in-process
  And one scenario fails with a clear assertion error
  And the install is rolled back
  And installed.json is NOT modified
  And the experiences silo is NOT restarted
  And the HTTP response is 400 with:
      - the failing scenario name
      - the Gherkin Given/When/Then text
      - the assertion that failed

When the user retries with /marketplace/install/Ino.Notes
Then the install proceeds normally
  (proving the failed install left no state corruption)
```

**Proves:** install-time BDD gate actually rejects bad experiences; rollback is clean; error reporting; subsequent installs unaffected. **Most important scenario for the "BDD guarantees behavior" claim.**

### Scenario-to-primitive coverage

| Primitive / feature | Covered by |
|---|---|
| Kernel silo boot + health | 1 |
| Marketplace HTTP endpoints | 1, 2, 10 |
| Install flow + consent | 2, 6, 10 |
| BDD install-time gate | 2, 10 |
| Aspire silo restart | 2, 10 |
| `ctx.Fire<T>` routing | 3, 4, 5 |
| `ctx.FireBroadcast<T>` fan-out | 4, 7 |
| `INeuron<T>` canonical dispatch | 3, 4, 6, 7 |
| `IReactsTo<T>` fan-out | 4 (AutoCheckIn reacts to TripPlanned) |
| JournaledGrain + LogStorage | 3, 5, 8 |
| `EventEnvelope<T>` causation | 4, 7, 8 |
| Level-1 search | 1, 2 |
| Level-3 memory search | 3, 5 |
| Identity vault + scoped grants | 6 |
| OAuth loopback callback | 6 |
| Orleans reminders + `FakeTimeProvider` | 7 |
| `IAmbientFire` synthesized context | 7 |
| Playback neuron | 8 |
| CausationIndex neuron | 8 |
| BranchManager + branch-scoped activation | 9 |
| OTel trace assertion (Aspire MCP) | 4, 7 |
| Rollback on install failure | 10 |

Every primitive has at least one covering scenario; several have redundant coverage for high-value paths.

## 22. Scale flags + deferred work

### 22.1 Things to verify before scaling past POC

- **LogStorage cliff** — the "rewrite the whole blob on every append" pattern hits ~50K events per grain. Specialized neurons stay well under, but the `CausationIndex` neuron will hit it earlier without decay. Track A ships decay consolidation for CausationIndex; other neurons defer until they approach the cliff.
- **Redis memory footprint** — each grain's event list serialized as one Redis key. With thousands of active grains each holding thousands of events, memory usage can surprise. Monitor in Aspire dashboard; plan for Redis Cluster or migration to an event store if we hit multi-GB.
- **Orleans reminder density** — each `AutoCheckIn.Watcher` instance holds pending reminders. Many concurrent user trips = many reminders. Orleans reminder storage (clustering table) has its own scaling properties; needs load testing.
- **Discovery grain single-threading** — `IDiscovery` in the `system` silo serializes all lookups through one grain activation. Lookup cache in each silo mitigates, but first-lookup latency after a cold silo is one RPC per new synapse type. Measure.

### 22.2 Deferred to follow-up specs

- **Track B — `ino.new` authoring UX + SDK + project templates.** Next spec after Track A.
- **Track C — self-improvement loop.** Reads Playback + CausationIndex for pattern extraction; authors new experiences automatically. Requires Track A primitives stable.
- **Track D — Flutter client rewire + persona visualization.** Uses the Flutter stub pattern (section 18) as its re-enablement point.
- **Track F — per-silo sandboxing + resource budgets + crash recovery policy.** Hardens the experiences silo against rogue marketplace code. Track A reserves the hooks (capability enforcement, OTel namespacing).
- **Track G — real marketplace.** Remote feed, verification pipeline, signing, revenue model, dynamic assembly loading. Track A's marketplace endpoints are the public contract; Track G swaps the implementation.

## 23. Embedded decisions summary

For quick reference when someone implementing Track A hits an ambiguous choice:

1. **Three kernel silos only** — `system`, `identity`, `experiences`. No `router`, no `timeline`, no per-experience silos.
2. **Direct peer-to-peer gRPC** between silos. Discovery via runtime `IDiscovery` grain in `system`. No composed manifest file.
3. **Experience is the product primitive; neuron + synapse are the runtime primitives.** User-written classes never carry `Neuron` or `Synapse` suffixes.
4. **Two handler interfaces: `INeuron<T>` (canonical, request/response) + `IReactsTo<T>` (reactive, fan-out).** Collision detection applies only to `INeuron<T>`.
5. **Orleans JournaledGrain + LogStorage + Redis** for neuron journals. `Neuron<TState, TEvent>` base class wraps this invisibly. Migration path is per-neuron `ICustomStorageInterface`.
6. **`EventEnvelope<T>` carries causation metadata.** Framework writes it; authors never see it in normal code.
7. **Playback + CausationIndex + BranchManager as neurons in `system` silo**, not a dedicated timeline silo.
8. **Identity silo lifts TripRadar's `User` + `UserProfile` pattern + adds `ExternalGrant` entity.** Postgres-backed.
9. **Marketplace POC = 6 HTTP endpoints in `system` silo + `installed.json` config file + Aspire silo restart.** No CLI. Track G swaps the implementation, not the contract.
10. **Install-time BDD gate via `Ino.Testing.InoTestHost` + `RecordedMockChatClient` + YAML recordings.** Same harness authors use during development.
11. **`AddExperiences<T>()` plural with bundle markers in `Ino.Bundles.*` namespace** and convention-based NuGet-prefix assembly scanning.
12. **Source generator produces `ExperienceMetadata`** from `[assembly: InoExperience]`, `[RequiresCapability]`, and `[UserEntry]` attributes. No hand-maintained manifest.
13. **Roslyn analyzer `Ino.Core.Hosting.Analyzers`** enforces 8 build-time rules including "no cross-experience `GrainFactory.GetGrain<>()`".
14. **OpenTelemetry shipped in Track A as layer 1 (local Aspire dashboard).** Layers 2 and 3 reserved with no new instrumentation required.
15. **Test strategy: 5 layers with strict speed budgets.** `InoTestSiloFixture` + `ICollectionFixture<T>` avoids TestCluster-per-class bloat.
16. **Flutter E2E code is stubbed via partial-class files excluded from `.csproj`.** Re-enablement is a 4-step recipe.
17. **10 canonical scenarios are the acceptance surface.** Track A is done when all 10 run green against the POC AppHost — no subjective criteria.
18. **Scenario 10 (broken experience rejection) is non-negotiable** — it's what makes "BDD guarantees behavior" real rather than aspirational.

---

## Appendix — file manifest for implementation planning

This section lists every file Track A must create, grouped by project. Implementation plans derived from this spec can check off against the manifest.

### `src/Ino.Core/`
- `ISynapse.cs`
- `NeuronResult.cs`
- `SynapseError.cs`
- `EventEnvelope.cs`
- `Capability.cs`
- `Attributes/UserEntryAttribute.cs`
- `Attributes/RequiresCapabilityAttribute.cs`
- `Attributes/InoExperienceAttribute.cs`
- `ExperienceMetadata.cs`

### `src/Ino.Core.Hosting/`
- `INeuron.cs`
- `IReactsTo.cs`
- `NeuronContext.cs`
- `Neuron.cs` (the base class)
- `IJournaledNeuronQuery.cs`
- `AddIno.cs` (extension method)
- `AddExperiences.cs` (extension method + assembly scanner)
- `Runtime/FireRuntime.cs` (the `ctx.Fire<T>` implementation)
- `Runtime/BroadcastRuntime.cs`
- `Runtime/DiscoveryClient.cs`
- `Runtime/IAmbientFire.cs` + implementation
- `Facades/SearchFacade.cs`
- `Facades/IdentityFacade.cs`
- `SourceGenerator/` (Roslyn source generator project)

### `src/Ino.Core.Hosting.Analyzers/`
- `InoAnalyzer.cs` with rules INO001–INO008

### `src/Ino.System/`
- `SystemChatService.cs` (gRPC endpoint)
- `SearchIndexer.cs` (background grain)
- Search grains: `DomainSearch`, `CapabilitySearch`, `MemorySearch<T>`, `IntentResolver`, `NeuronsIntrospection`
- `Playback.cs`
- `CausationIndex.cs` + `CausationIndexDecayJob.cs`
- `BranchManager.cs`
- `Discovery.cs` (the discovery grain)
- `Marketplace/MarketplaceInstaller.cs`
- `Marketplace/HttpEndpoints.cs` (six endpoints)
- `Marketplace/InstalledSet.cs`

### `src/Ino.Identity/` + `src/Ino.Identity.Domain/` + `src/Ino.Identity.Infrastructure/`
- `User.cs` aggregate (port from TripRadar)
- `UserProfile.cs` entity (port)
- `ExternalGrant.cs` entity (new)
- `ExternalOAuthOrchestrator.cs` (port + generalize GoogleAuthenticationOrchestrator)
- `IdentityVault.cs`
- Identity silo grain neurons: `RequireIdentity`, `GetIdentity`, `Revoke`, `ConsentGrantedReactor`, `ConsentDeniedReactor`
- EF Core configurations + migrations
- `OAuthCallbackEndpoint.cs`

### `src/Ino.Experiences/`
- Silo bootstrap + grain class discovery
- `Program.cs`

### `src/Ino.Testing/`
- `InoTestHost.cs`
- `InoTestSiloFixture.cs`
- `InoTestContext.cs` (Reqnroll-friendly facade)
- `RecordedMockChatClient.cs`
- `RecordingChatClient.cs` (records real LLM calls to YAML)
- `InoTestLlm.cs`
- `InoTestIdentity.cs` (stub vault)
- Reqnroll step bindings for common assertions

### `src/Ino.AppHost/`
- `AppHost.cs` (composes everything)
- `InstalledSet.cs` (reads `~/.ino/installed.json`)
- `Extensions/InoBuilderExtensions.cs`

### `contracts/`
- `Ino.Contracts.System/` — `UserIntent`, `SearchQuery<T>`, `DomainsQuery`, `CapabilityQuery`, `MemoryQuery<T>`, `IntentResolved`, `BrowserOpenRequested`
- `Ino.Contracts.Identity/` — `RequireIdentityRequest`, `GetIdentityRequest`, `RevokeRequest`, `ConsentRequested`, `ConsentGranted`, `ConsentDenied`, `ReauthenticationRequired`, `ExternalGrantAdded`, `IdentityGranted`, `IdentityRevoked`
- `Ino.Contracts.Playback/` — `WalkBackwardRequest`, `WalkForwardRequest`, `CorrelationTraceRequest`, `EventLinked`, `CausalChain`, `CausalTree`, `CorrelationTrace`, `CreateBranchRequest`, `ListBranchesRequest`, `DeleteBranchRequest`, `BranchInfo`

### `experiences/notes/`
- `Ino.Notes/` — meta-package marker
- `manager/Ino.Notes.Manager/` — `NotesManager` grain class, `NotesState`
- `manager/Ino.Notes.Manager.Contracts/` — `CreateNote`, `ListNotes`, `DeleteNote`, `NoteCreated`
- `manager/Ino.Notes.Manager.Tests/` — Reqnroll feature files + step definitions + `mocks/llm.recordings.yml` (empty)

### `experiences/travel/`
- `Ino.Travel/` — meta-package marker
- `flight-search/` — FlightSearch implementation + contracts + tests
- `hotel-search/` — HotelSearch implementation + contracts + tests
- `place-discovery/` — PlaceDiscovery implementation + contracts + tests
- `trip-planner/` — TripPlanner implementation + contracts + tests
- `auto-check-in/` — AutoCheckIn (Watcher + CheckInAgent) + contracts + tests

### `test/`
- `Ino.Core.Tests/` — primitive unit tests
- `Ino.System.Tests/` — system silo integration
- `Ino.Identity.Tests/` — identity silo integration
- `Ino.Hosting.Tests/` — `ctx.Fire<T>` runtime tests
- `Ino.Bdd/` — cross-experience Reqnroll scenarios
- `Ino.E2E/` — Playwright-stubbed scenarios (10 total)
- `Ino.E2E/Infrastructure/NeuronE2ETest.cs` + `GrpcTestFixture.cs` (compiled)
- `Ino.E2E/Infrastructure/NeuronE2ETest.Flutter.cs` + `GrpcTestFixture.Flutter.cs` (excluded)
- `Ino.E2E/Infrastructure/README.md` (re-enablement recipe)

---

**This spec is the design. Implementation plans derived from it are produced via `superpowers:writing-plans`.**
