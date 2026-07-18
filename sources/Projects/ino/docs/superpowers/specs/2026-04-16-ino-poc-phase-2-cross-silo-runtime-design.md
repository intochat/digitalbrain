# ino POC Phase 2 — cross-silo runtime + AppHost + marketplace scaffold (design)

**Date:** 2026-04-16
**Scope:** greenfield POC at `D:\ino\POC\` — **does not modify `D:\ino\src\`**
**Status:** design locked in brainstorming session 2026-04-16; ready for implementation planning
**Builds on:** Phase 1 (`docs/superpowers/specs/2026-04-14-ino-poc-core-primitives-design.md` §§ 5, 7, 9; `docs/superpowers/plans/2026-04-14-ino-poc-phase-1-core-foundations-plan.md`)

---

## 1. Goal

Prove ino's architectural thesis that **neurons hosted by separate silos dispatch typed synapses to each other via `ctx.Fire<T>()` with zero hand-rolled transport code**, that **experiences are first-class typed abstractions installed/uninstalled via an Aspire-style extension method**, and that **a marketplace HTTP surface can trigger silo restarts to reflect install state changes**.

Phase 1 shipped the primitive contract (`Neuron<TEvent>` + `ISynapse` + `IReactsTo<T>` + `INeuron<T>` + journal-is-state). Phase 2 makes those primitives runnable across process boundaries, with the topology the eventual production deployment will use, minus every piece of infrastructure that is knowable-from-the-parent-repo.

## 2. North-star — what "done" looks like

A developer clones `D:\ino\POC\`, runs `aspire start`, sees three silo resources reach healthy state in the Aspire dashboard, and can:

1. `POST /marketplace/install/Ino.Testing.Fixture.Alpha` → watches the `experiences` silo restart in the dashboard → Alpha's grains are now live.
2. `POST /marketplace/install/Ino.Testing.Fixture.Beta` → restart again → Alpha can fire `PingBeta` at Beta and get a pong response.
3. Open the Aspire traces tab → sees one `fire Ino.Testing.Fixture.Beta.Contracts.PingBeta` producer span in the experiences silo, linked via W3C traceparent to a sibling `handle` consumer span also in the experiences silo (or in system, for cross-silo scenarios).

Zero hand-rolled protobuf. Zero hand-rolled gRPC client/server. Zero Redis. Zero Postgres. Zero OAuth. The architecture holds on Orleans' native routing and Aspire's native composition.

## 3. Scope

### 3.1 In

- Three Orleans silo processes joined as one cluster: `system`, `identity` (stub), `experiences`
- Aspire AppHost composing the three silos and exposing `aspire start` / `aspire stop` / per-silo restart
- `IExperience` abstraction + `WithExperience<T>()` extension method on the Aspire builder
- `~/.ino/installed.json` conditional wiring
- `Discovery` grain in the `system` silo — singleton, integer-keyed, in-memory registry rebuilt at silo startup
- `NeuronContext` as a sealed record with `Fire` / `FireBroadcast` / causation metadata / logger
- Cross-silo dispatch via Orleans' native grain-call routing (no custom gRPC for intra-ino transport)
- `IAmbientFire` for Orleans reminders / startup tasks / `RaiseAsync`'s `EventLinked` push
- Capability enforcement stub against `IExperience.DeclaredCapabilities`
- Six marketplace HTTP endpoints on the `system` silo (one returns 501 for Phase 5 consent flow)
- Aspire `ResourceCommandService` restart hook plumbed into `POST /marketplace/install`
- Four test-fixture experiences: `Alpha`, `Beta`, `Gamma` (capability-denial), `Delta` (reactive fan-out)
- `SystemEcho` neuron hosted by the `system` silo — minimal proof of system-silo-hosted handlers
- Typed identity primitives: `BundleId`, `KernelSilo`, `Caller`, `SynapseErrorCode`, `EventId`, `CorrelationId`, `SynapseId`, `StreamKey`, `Type`-based grain references, `Telemetry` / `InoPaths` / `AspireCommands` constant classes
- L1 through L5 test coverage, 16 canonical scenarios across cross-silo dispatch, capability enforcement, ambient fire, conditional wiring, marketplace, fan-out
- PR #9 findings I1, I2, I4, I5, I6, I10 folded in

### 3.2 Out — deferred to later phases

| Deferred piece | Phase |
|---|---|
| Redis grain storage via `Microsoft.Orleans.Persistence.Redis` | Phase 5+ |
| Redis clustering provider (replaces `UseLocalhostClustering`) | Phase 5+ |
| Real `identity` silo (TripRadar pattern, Postgres, BCrypt, OAuth, `ExternalGrant`) | Phase 5 |
| Source generator + Roslyn analyzer (`INO001`–`INO008`) | Phase 3 |
| Auto-aggregated `ExperienceMetadata` record | Phase 3 |
| `ctx.Search` facade on `NeuronContext` | Phase 4 |
| `ctx.Identity` facade on `NeuronContext` | Phase 5 |
| Marketplace two-step consent with `consent_token` | Phase 5 |
| Marketplace BDD install gate | Phase 5 |
| Real `~/.ino/marketplace.json` feed entries (non-fixture bundles) | Phase 5 |
| `Playback` / `CausationIndex` / `BranchManager` neurons | Phase 6 |
| Per-grain capability declarations beyond `IExperience.PerGrainCapabilities` | Phase 3 (source gen supplements) |
| Multi-assembly experience bundles (`Ino.Travel` pulling in `Ino.Travel.FlightSearch`) | Phase 3 |
| PR #9 findings I3, I7, I8, I9, I11, M1-M9 | Dedicated cleanup branch |

## 4. Architecture overview

### 4.1 Process topology

One Aspire `DistributedApplication` composes three Orleans silo processes:

| Silo | Project | Hosts | Lifecycle |
|---|---|---|---|
| `system` | `Ino.System.Host` | `Discovery` grain, `SystemEcho` neuron, marketplace controller (ASP.NET), `ExperienceRestartService` | Always on |
| `identity` | `Ino.Identity.Host` | Empty — joins the cluster, hosts zero grains, exists so Phase 5 is additive | Always on |
| `experiences` | `Ino.Experiences.Host` | All grains from installed `IExperience` bundles | Restarts on `POST /marketplace/install` / `uninstall` |

### 4.2 Cluster topology

All three silos join **one** Orleans cluster via `UseLocalhostClustering`. Orleans' own grain-call routing delivers cross-silo calls transparently — we write no hand-rolled transport. The `system` silo's placement director keeps the `Discovery` grain and `SystemEcho` local; the `experiences` silo hosts everything else.

This is a deliberate divergence from the Phase-1 spec's §10.2 "two separate clusters + gRPC" design. POC Option A chose Orleans-native for:
- Zero protobuf / gRPC boilerplate for internal transport
- Matches Aspire's single-cluster mental model
- Independent silo restart still works — Orleans drains activations gracefully when a silo exits the cluster and rejoins

Phase 5 may split to multiple clusters if independent deploy lifecycle becomes load-bearing; the `Discovery` abstraction does not change shape in that transition.

### 4.3 Storage

- Orleans clustering: `UseLocalhostClustering`
- Grain storage: `MemoryGrainStorage`
- State machine storage: `VolatileStateMachineStorageProvider` (as Phase 1)

All state is process-lifetime. Silo restart loses all journals. Acceptable for Phase 2 because every architectural claim Phase 2 proves is transport/routing/packaging; nothing depends on persistence surviving restart. Phase 5 swaps the providers; the rest of the architecture is unchanged.

### 4.4 External transport (out of scope)

Flutter-client gRPC / gRPC-Web, Telegram bot HTTP, MCP server — all external transports that already exist in `D:\ino\src\` — are **not** part of Phase 2. Phase 2's single external surface is the marketplace HTTP controller in the `system` silo.

## 5. Typed identity primitives — `Ino.Core` / `Ino.Core.Hosting`

Strings are only acceptable at external boundaries (Orleans grain-class names, Aspire resource names, OTel attribute keys, file paths, JSON text, LLM prompts). Inside ino's architectural code, identities are typed.

### 5.1 Value types

```csharp
namespace Ino.Core;

public readonly record struct BundleId(string Value)
{
    public override string ToString() => Value;
    public static BundleId From(string value) => new(Validate(value));
    private static string Validate(string value) =>
        !string.IsNullOrWhiteSpace(value) ? value
        : throw new ArgumentException("BundleId cannot be empty.", nameof(value));
}

public readonly record struct SynapseId(string Value)     { public static SynapseId New() => new(Ulid.NewUlid().ToString()); }
public readonly record struct CorrelationId(string Value) { public static CorrelationId New() => new(Ulid.NewUlid().ToString()); }
public readonly record struct EventId(string Value)       { public static EventId New() => new(Ulid.NewUlid().ToString()); }
public readonly record struct StreamKey(string Value);
```

### 5.2 `KernelSilo` enum

```csharp
namespace Ino.Core.Hosting;

public enum KernelSilo { System, Identity, Experiences }

public static class KernelSiloExtensions
{
    public static string ToResourceName(this KernelSilo silo) => silo switch
    {
        KernelSilo.System       => "system",
        KernelSilo.Identity     => "identity",
        KernelSilo.Experiences  => "experiences",
        _ => throw new UnreachableException()
    };
}
```

### 5.3 `Caller` discriminated union

```csharp
public abstract record Caller
{
    public sealed record FromBundle(BundleId Bundle) : Caller;
    public sealed record Ambient(KernelSilo Silo) : Caller;
}
```

Replaces every `"<ambient>"` / `"Ino.Travel"` string occurrence in architectural code.

### 5.4 `SynapseErrorCode` enum + `SynapseError` record

```csharp
public enum SynapseErrorCode
{
    NoCanonicalHandler,
    CapabilityDenied,
    DiscoveryConflict,
    GrainActivationFailed,
    Cancelled,
}

public sealed record SynapseError(
    SynapseErrorCode Code,
    string Message,
    IReadOnlyDictionary<string, string>? Details = null);
```

Wire/log serialization writes the enum's string name. In-memory code always holds the enum value.

### 5.5 `Type`-based grain references

```csharp
public sealed record CanonicalTarget(
    Type SynapseType,
    Type GrainType,
    BundleId Bundle,
    IReadOnlyList<Capability> RequiredCapabilities);

public sealed record ReactiveTarget(
    Type SynapseType,
    Type GrainType,
    BundleId Bundle);
```

Conversion to Orleans' `grainClassNamePrefix` string happens in exactly one place — the `FirePort` call to `GrainFactory.GetGrain(...)`.

### 5.6 Well-known grain keys

`IDiscovery : IGrainWithIntegerKey` activated with key `0`. Resolution via a typed helper:

```csharp
public static class GrainFactoryExtensions
{
    public static IDiscovery GetDiscovery(this IGrainFactory grains) => grains.GetGrain<IDiscovery>(0);
}
```

### 5.7 Authorized-string constant classes

```csharp
public static class InoPaths
{
    public static string InstalledJson => Path.Combine(Home, ".ino", "installed.json");
    public static string MarketplaceJson => Path.Combine(Home, ".ino", "marketplace.json");
    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}

public static class Telemetry
{
    public const string ActivitySourceName = "ino";
    public const string MeterName = "ino";

    public static class Tags
    {
        public const string SynapseType     = "ino.synapse.type";
        public const string SourceBundle    = "ino.source.bundle";
        public const string TargetBundle    = "ino.target.bundle";
        public const string CorrelationId   = "ino.correlation_id";
        public const string ResultSuccess   = "ino.result.success";
        public const string ErrorCode       = "ino.error.code";
    }

    public static class Spans
    {
        public static string Fire(Type synapseType)    => $"fire {synapseType.FullName}";
        public static string Handle(Type synapseType)  => $"handle {synapseType.FullName}";
        public static string React(Type synapseType)   => $"react {synapseType.FullName}";
    }
}

public static class AspireCommands
{
    public const string Rebuild = "rebuild";
    public const string Restart = "restart";
}
```

### 5.8 Marketplace feed records (external-boundary typed deserialization)

```csharp
public sealed record MarketplaceFeed(IReadOnlyList<MarketplaceFeedEntry> Experiences);
public sealed record MarketplaceFeedEntry(BundleId Id, string Description, string Version);

public sealed record InstalledState(IReadOnlyList<BundleId> Installed);
```

Custom `JsonConverter<BundleId>` converts the string value at the JSON boundary.

## 6. `IExperience` — the first-class bundle abstraction

### 6.1 Interface

```csharp
namespace Ino.Core.Hosting;

public interface IExperience
{
    BundleId Bundle { get; }
    string Version { get; }
    IReadOnlyList<Capability> DeclaredCapabilities { get; }

    // Optional — bundles without per-grain detail return an empty dictionary.
    // Phase 2 enforcement is bundle-level; Phase 3 source gen may populate this automatically.
    IReadOnlyDictionary<Type, IReadOnlyList<Capability>> PerGrainCapabilities
        => ImmutableDictionary<Type, IReadOnlyList<Capability>>.Empty;
}
```

### 6.2 Example implementation

```csharp
namespace Ino.Experiences;

public sealed class Travel : IExperience
{
    public BundleId Bundle => BundleId.From("Ino.Travel");
    public string Version => "1.0.0";

    public IReadOnlyList<Capability> DeclaredCapabilities =>
    [
        new Capability.Http("serpapi.com"),
        new Capability.Llm(LlmTier.Reasoning),
    ];

    public IReadOnlyDictionary<Type, IReadOnlyList<Capability>> PerGrainCapabilities =>
        new Dictionary<Type, IReadOnlyList<Capability>>
        {
            [typeof(SerpFlightSearch)] = [new Capability.Http("serpapi.com"), new Capability.Llm(LlmTier.Reasoning)],
            [typeof(TripPlanner)]      = [new Capability.Llm(LlmTier.Reasoning)],
        };
}
```

One file per bundle tells the whole story. No attribute reflection anywhere.

### 6.3 Neuron discovery

Grain types are discovered by scanning the experience's assembly for `INeuron<>` / `IReactsTo<>` implementations — pure type-system query. No `[Neuron]` attribute. The type system already tells us.

### 6.4 Why E2E is simpler with `IExperience`

Tests instantiate experiences as plain objects:

```csharp
var alpha = new Ino.Testing.Fixture.Alpha();
var beta  = new Ino.Testing.Fixture.Beta();

await using var host = await InoTestAppHost.BuildAsync(alpha, beta);
var result = await host.FireFromSystemAsync(new PingAlpha("hello"));
```

The subject under test — "this experience, as a unit" — is a type the test constructs directly. No `typeof(Travel).GetCustomAttribute<ExperienceAttribute>()`.

## 7. Experience packaging — `WithExperience<T>()` + `installed.json`

### 7.1 Developer-facing shape

A domain author publishes two NuGet packages:

```
Ino.Travel                    implementation — sealed grain classes + IExperience impl
Ino.Travel.Contracts          synapse records only — references Ino.Core
```

The AppHost author writes:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var ino = builder.AddIno("ino")
    .WithExperience<Notes>()      // IExperience impl from Ino.Notes
    .WithExperience<Travel>();    // IExperience impl from Ino.Travel

builder.Build().Run();
```

Muscle memory equivalent to `builder.AddRedis("cache")`. One call per bundle.

### 7.2 The extension method

```csharp
namespace Ino.Aspire.Hosting;

public static class InoBuilderExtensions
{
    public static IInoBuilder WithExperience<T>(this IInoBuilder builder)
        where T : class, IExperience, new()
    {
        var experience = new T();
        builder.RegisterExperience(experience);
        return builder;
    }
}
```

Generic constraint enforces shape; the compiler rejects non-experience types.

### 7.3 `installed.json` gate

At AppHost build time, `WithExperience<T>()` consults `InstalledSet.Load(InoPaths.InstalledJson)`:

- If the bundle id is in the installed set → register with the experiences silo's grain assembly configuration.
- Otherwise → no-op. The author's `Program.cs` lists every *available* bundle; runtime decides which wire up.

Marketplace `POST /install/{id}` writes to `installed.json` + triggers `experiences` silo restart. The restart re-runs the AppHost wiring with the updated set.

### 7.4 Multi-assembly bundle fan-out (deferred)

Phase 2 ships one bundle = one assembly = one `IExperience` impl. Multi-assembly bundles (e.g. `Ino.Travel` meta-package referencing `Ino.Travel.FlightSearch`, `Ino.Travel.TripPlanner`) arrive in Phase 3.

## 8. `NeuronContext` — sealed record

```csharp
namespace Ino.Core.Hosting;

public sealed record NeuronContext(
    SynapseId SynapseId,
    CorrelationId CorrelationId,
    Caller Source,
    StreamKey SourceStream,
    string? UserId = null,
    string? SessionId = null)
{
    public required IFirePort FirePort { get; init; }
    public required ILogger Logger { get; init; }
    public Activity? CurrentActivity { get; init; }
    public EventId? CurrentEventId { get; init; }   // set by Neuron<TEvent>.RaiseAsync

    public Task<NeuronResult> Fire<T>(T synapse, CancellationToken ct = default) where T : ISynapse
        => FirePort.Fire(synapse, this, ct);

    public Task FireBroadcast<T>(T synapse, CancellationToken ct = default) where T : ISynapse
        => FirePort.FireBroadcast(synapse, this, ct);
}
```

Resolves PR #9 I6. `with`-expressible for tests. `required` on the port and logger means every construction site declares its wiring — no silent `null` dependencies. The `Fire<T>` / `FireBroadcast<T>` methods on the record forward to the port, passing `this` as the caller context — call sites write `ctx.Fire<MyEvent>(payload)` and the port receives the full context without needing an `AsyncLocal` or construction-time coupling.

```csharp
public interface IFirePort
{
    Task<NeuronResult> Fire<T>(T synapse, NeuronContext caller, CancellationToken ct = default) where T : ISynapse;
    Task FireBroadcast<T>(T synapse, NeuronContext caller, CancellationToken ct = default) where T : ISynapse;
}
```

`IFirePort` is a singleton service in each silo. The context parameter carries caller identity per call — no per-activation scoping, no construction cycle between `NeuronContext` and `FirePort`.

Test construction via a factory in `Ino.Testing`:

```csharp
public static class NeuronContextForTest
{
    public static NeuronContext Create(
        Caller source,
        IFirePort? firePort = null,
        ILogger? logger = null)
    {
        return new NeuronContext(
            SynapseId.New(),
            CorrelationId.New(),
            source,
            new StreamKey("test"))
        {
            FirePort = firePort ?? new NoOpFirePort(),
            Logger = logger ?? NullLogger.Instance,
        };
    }
}
```

Phase 1's `InoTestNeuronContext` class is replaced by this factory.

## 9. `Discovery` grain + registration flow

### 9.1 Interface

```csharp
namespace Ino.System;

public interface IDiscovery : IGrainWithIntegerKey
{
    Task RegisterAsync(SiloRegistration registration, CancellationToken ct = default);
    Task<CanonicalTarget?> LookupCanonicalAsync(Type synapseType, CancellationToken ct = default);
    Task<IReadOnlyList<ReactiveTarget>> LookupReactiveAsync(Type synapseType, CancellationToken ct = default);
    Task<DiscoveryDump> DumpAsync(CancellationToken ct = default);
}

public sealed record SiloRegistration(
    KernelSilo Silo,
    IReadOnlyList<CanonicalRegistration> Canonical,
    IReadOnlyList<ReactiveRegistration> Reactive);

public sealed record CanonicalRegistration(
    Type SynapseType, Type GrainType, BundleId Bundle,
    IReadOnlyList<Capability> RequiredCapabilities);

public sealed record ReactiveRegistration(
    Type SynapseType, Type GrainType, BundleId Bundle);
```

### 9.2 Registration flow

`Ino.Core.Hosting` ships an `IHostedService` that runs at silo startup in every silo:

1. Reflect over loaded application parts; find all `INeuron<>` / `IReactsTo<>` implementations.
2. For each implementation, determine its owning `IExperience` (the bundle whose assembly contains the grain type). Read `experience.PerGrainCapabilities[grainType]` for required capabilities (empty list if absent).
3. Build `SiloRegistration` and call `grainFactory.GetDiscovery().RegisterAsync(...)`. Orleans routes the call to the `system` silo where the `Discovery` grain activation lives.
4. On collision (`DiscoveryConflictException`), the hosted service throws from `StartAsync` — the silo fails to start, Aspire surfaces `Failed` state on the resource.

### 9.3 Collision detection

For each `(SynapseType, is-canonical)` pair, only one registration is accepted across the whole cluster. A second canonical registration for the same synapse type throws with:

```
DiscoveryConflictException:
  {SecondGrainType} in silo {SecondSilo} cannot register as canonical handler for
  {SynapseType.FullName} — already registered to {FirstGrainType} in silo {FirstSilo}.
```

Reactive registrations never collide — many listeners per synapse type is the design.

### 9.4 Routing cache

Each silo's `Ino.Core.Hosting` caches canonical/reactive lookups in-memory. Cache is cleared on silo restart (which is the only way the registry changes). No explicit invalidation API.

### 9.5 Debug endpoint

`GET /discovery/table` on the `system` silo returns `IDiscovery.DumpAsync()` as JSON — canonical registrations, reactive registrations, per-silo grouping. Useful for debugging install/uninstall flows.

## 10. Fire runtime + `IAmbientFire` + capability enforcement

### 10.1 `FirePort` implementation

```csharp
internal sealed class FirePort(
    IGrainFactory grains,
    IDiscoveryClient discovery,
    ICapabilityEnforcer capabilityEnforcer,
    ActivitySource activitySource) : IFirePort
{
    public async Task<NeuronResult> Fire<T>(T synapse, NeuronContext caller, CancellationToken ct) where T : ISynapse
    {
        var target = await discovery.LookupCanonicalAsync(typeof(T), ct);
        if (target is null)
            return NeuronResult.Fail(new SynapseError(
                SynapseErrorCode.NoCanonicalHandler,
                $"No installed bundle implements INeuron<{typeof(T).Name}>."));

        try
        {
            capabilityEnforcer.AssertCanFire(caller.Source, target);
        }
        catch (CapabilityDeniedException ex)
        {
            return NeuronResult.Fail(new SynapseError(
                SynapseErrorCode.CapabilityDenied, ex.Message, ex.Details));
        }

        using var span = activitySource.StartActivity(
            Telemetry.Spans.Fire(typeof(T)), ActivityKind.Producer);
        span?.SetTag(Telemetry.Tags.SynapseType, typeof(T).FullName);
        span?.SetTag(Telemetry.Tags.SourceBundle, caller.Source is Caller.FromBundle b ? b.Bundle.Value : null);
        span?.SetTag(Telemetry.Tags.TargetBundle, target.Bundle.Value);
        span?.SetTag(Telemetry.Tags.CorrelationId, caller.CorrelationId.Value);

        var grain = grains.GetGrain<INeuron<T>>(
            grainKey: caller.CorrelationId.Value,
            grainClassNamePrefix: target.GrainType.FullName);
        var result = await grain.HandleAsync(synapse, DeriveChildContext(caller, target), ct);

        span?.SetTag(Telemetry.Tags.ResultSuccess, result.Success);
        if (!result.Success && result.Error is { } err)
            span?.SetTag(Telemetry.Tags.ErrorCode, err.Code.ToString());

        return result;
    }

    public async Task FireBroadcast<T>(T synapse, NeuronContext caller, CancellationToken ct) where T : ISynapse
    {
        var targets = await discovery.LookupReactiveAsync(typeof(T), ct);
        if (targets.Count == 0) return;   // zero-listener broadcast is not an error

        await Parallel.ForEachAsync(targets, ct, async (target, inner) =>
        {
            capabilityEnforcer.AssertCanFireBroadcast(caller.Source, target);
            var grain = grains.GetGrain<IReactsTo<T>>(
                grainKey: caller.CorrelationId.Value,
                grainClassNamePrefix: target.GrainType.FullName);
            await grain.ReactAsync(synapse, DeriveChildContext(caller, target), inner);
        });
    }
}
```

The runtime does not branch on silo location. Orleans' placement director routes to whichever silo hosts the grain activation. If both caller and target are in the same silo, Orleans takes the in-process fast path; if they're in different silos, Orleans sends the call over its cluster-internal transport. We write no routing code.

### 10.2 `IAmbientFire`

```csharp
public interface IAmbientFire
{
    Task<NeuronResult> FireAsync<T>(T synapse, CorrelationId? correlationId = null, CancellationToken ct = default) where T : ISynapse;
    Task FireBroadcastAsync<T>(T synapse, CorrelationId? correlationId = null, CancellationToken ct = default) where T : ISynapse;
}
```

Singleton per silo. Internally synthesizes a `NeuronContext` with `Source = new Caller.Ambient(thisSilo)`, a fresh `CorrelationId` if none supplied, no `UserId`/`SessionId`. Uses the same `FirePort`.

Used by:
- Orleans reminders within any silo
- Silo startup hosted services (e.g. the registration flow itself uses direct grain calls, not ambient fire, but future startup tasks may)
- `Neuron<TEvent>.RaiseAsync` for the Phase 6 `EventLinked` push (not exercised in Phase 2 but the plumbing is present)

### 10.3 Capability enforcement stub

```csharp
public interface ICapabilityEnforcer
{
    void AssertCanFire(Caller source, CanonicalTarget target);
    void AssertCanFireBroadcast(Caller source, ReactiveTarget target);
}
```

Phase 2 implementation reads a static in-memory map built at silo startup from `IExperience.DeclaredCapabilities` of every registered bundle. For `Caller.Ambient(silo)`, all capabilities are granted (silo-level trust). For `Caller.FromBundle(id)`, the enforcer looks up the declared set.

Check for canonical fire: `target.RequiredCapabilities ⊆ sourceBundle.DeclaredCapabilities`. Mismatch → `CapabilityDeniedException` → `FirePort` converts to `NeuronResult.Fail(SynapseErrorCode.CapabilityDenied, ...)`.

Reactive fire does not check per-target capabilities in Phase 2 — fan-out is opt-in on the listener side; capability model for reactive will get more nuance in Phase 3 when source gen lands.

## 11. Marketplace scaffold

### 11.1 Endpoints

Hosted by the `system` silo's ASP.NET server:

| Method | Path | Purpose |
|---|---|---|
| `GET`  | `/marketplace/available` | List entries from `InoPaths.MarketplaceJson` |
| `GET`  | `/marketplace/available/{id}` | Single entry or 404 |
| `GET`  | `/marketplace/installed` | List bundles in `InoPaths.InstalledJson` |
| `POST` | `/marketplace/install/{id}` | Install (200 / 404 / 409 / 504) |
| `POST` | `/marketplace/install/{id}/consent` | Returns 501 — Phase 5 |
| `POST` | `/marketplace/uninstall/{id}` | Uninstall (200 / 404) |
| `GET`  | `/discovery/table` | JSON dump of `IDiscovery.DumpAsync()` |

### 11.2 Install flow

```
POST /marketplace/install/{id}
  1. Read marketplace feed. Verify id exists.                                         → else 404
  2. Read installed state. Verify id NOT already installed.                           → else 409
  3. Atomically update installed.json (temp-file + rename).
  4. await _resourceCommandService.ExecuteCommandAsync(
         KernelSilo.Experiences.ToResourceName(), AspireCommands.Rebuild, ct);
  5. await _resourceNotifications.WaitForResourceHealthyAsync(
         KernelSilo.Experiences.ToResourceName(), cts.Token);   // 60s timeout
  6. Return 200 + { status: "installed", installed: [...] }.
```

Atomic write avoids a half-written `installed.json` on process crash. The 60-second timeout exists because a `DiscoveryConflictException` on the restarting silo will hold the resource in `Failed` state — the endpoint needs to surface that rather than hang.

### 11.3 Error surfaces

| Condition | Status | Body |
|---|---|---|
| Unknown id on install/uninstall | 404 | `{ "status": "not_found", "id": "..." }` |
| Already-installed on install | 409 | `{ "status": "already_installed", "id": "..." }` |
| Not-installed on uninstall | 404 | `{ "status": "not_installed", "id": "..." }` |
| `installed.json` IO failure | 500 | `{ "status": "state_write_failed", "detail": "..." }` |
| Experiences silo fails to restart | 504 | `{ "status": "restart_failed", "detail": "...", "aspire_resource_state": "Failed" }` |
| Consent endpoint | 501 | `{ "status": "not_implemented", "phase": "Phase 5" }` |

### 11.4 Concurrency

A `SemaphoreSlim(1, 1)` in the marketplace controller serializes `POST /install` and `POST /uninstall` calls. Simple and correct for POC; distributed coordination is a Phase 5+ concern if marketplace ever scales beyond one `system` silo.

### 11.5 `ResourceCommandService` integration

The system silo's ASP.NET host registers Aspire's `ResourceCommandService` + `ResourceNotificationService` via a hosting extension. The marketplace controller takes both as constructor dependencies. Exact package + registration surface verified via Context7 during implementation (see §14).

## 12. Test strategy

### 12.1 Layers

| Layer | Project | Scope | Speed target |
|---|---|---|---|
| **L1** | `Ino.Core.Tests` + `Ino.Core.Hosting.Tests` (existing, extended) | Primitive types, `NeuronResult.TryGetPayload`, causation envelope mapping, `IExperience` property checks, capability immutability | <5s |
| **L2** | `Ino.System.Tests` (new) | `Discovery` grain collision + lookup + dump; marketplace controller with mocked `ResourceCommandService`; JSON file round-trips | <30s |
| **L2** | `Ino.Experiences.Tests` (new) | `FirePort` runtime with stub grain factory; capability enforcer; `IAmbientFire` context synthesis | <30s |
| **L3** | `Ino.Hosting.Tests` (new) | Multi-silo `TestCluster` via `InoMultiSiloFixture` — two silos, one cluster, real `Discovery` grain, cross-silo dispatch | <60s |
| **L5** | `Ino.E2E.Tests` (new) | `DistributedApplicationTestingBuilder` — real Aspire AppHost, HTTP to marketplace, full install → restart → cross-silo fire | ~3 min |

Total Phase 2 suite target: **<5 minutes from clean**.

### 12.2 Test-fixture experiences

Four `IExperience` implementations in `experiences/testing/`:

| Fixture | Role | Declared capabilities |
|---|---|---|
| `Ino.Testing.Fixture.Alpha` | `INeuron<PingAlpha>`; fires `PingBeta` at Beta and returns aggregated result | `Llm:Default` |
| `Ino.Testing.Fixture.Beta` | `INeuron<PingBeta>`; returns `PingResponse("pong from beta")` | `Llm:Default` |
| `Ino.Testing.Fixture.Gamma` | Same shape as Alpha but declares only `Llm:Default` while Beta's version requires `Llm:Reasoning` → triggers capability denial | `Llm:Default` |
| `Ino.Testing.Fixture.Delta` | Two reactive listeners `DeltaFirstListener` and `DeltaSecondListener` both on `IReactsTo<SomethingObserved>` — proves fan-out and multi-grain bundles | `Llm:Default` |

All ship as NuGet-style project pairs (`<Name>` + `<Name>.Contracts`).

### 12.3 `SystemEcho` — system-silo-hosted built-in

`Ino.System.SystemEcho : INeuron<EchoRequest>` lives in the `system` silo. Not an experience, not in `installed.json` — it's a built-in registered by the system-silo startup task directly. Returns `EchoResponse` with a `"[from system]"` prefix.

Exists to prove the `experiences → system` fire direction in Phase 2. Slated for removal when a real system-silo neuron lands (Phase 4 search, Phase 6 Playback).

### 12.4 `IInoTestCapture` — typed verification seam

```csharp
public interface IInoTestCapture
{
    void Record(Type grainType, ISynapse synapse);
    IReadOnlyList<CaptureEntry> Entries { get; }
    void Clear();
}

public sealed record CaptureEntry(Type GrainType, Type SynapseType, ISynapse Payload, DateTimeOffset At);
```

Delta's listeners (and any future fixture that needs to record state for assertion) write to this singleton. Tests assert via `Type` comparisons. No string grain-class-name matching anywhere.

### 12.5 Canonical scenarios (16 total — the spine of Phase 2 verification)

| # | Scenario | Proves | Layer |
|---|---|---|---|
| 1 | Two canonical handlers for the same synapse type registered in different silos → startup fails with `DiscoveryConflictException` | Collision detection | L3 |
| 2 | `ctx.Fire<PingBeta>` from Alpha reaches Beta; result envelope carries correct `CausedByEventId` + `CorrelationId` + `TraceParent` | Fire path + causation propagation | L3 |
| 3 | `SystemEcho` fires `PingAlpha` via `IAmbientFire` → reaches Alpha → result returns | system → experiences cross-silo + ambient fire | L3 |
| 4 | Alpha fires `EchoRequest` → reaches `SystemEcho` → response returns | experiences → system cross-silo | L3 |
| 5 | Gamma fires `PingBeta` with declared `Llm:Default` when `PerGrainCapabilities[BetaHandler]` requires `Llm:Reasoning` → `NeuronResult.Fail(CapabilityDenied)` | Capability enforcement | L2 |
| 6 | `installed.json` excluding Beta → `WithExperience<Beta>()` no-ops → Alpha's fire at Beta fails with `NoCanonicalHandler` | Conditional wiring | L5 |
| 7 | `POST /marketplace/install/Beta` → experiences silo restarts → subsequent `fire PingBeta` succeeds | Install + restart hook end-to-end | L5 |
| 8 | `POST /marketplace/install/unknown-id` → 404; already-installed → 409; missing-on-uninstall → 404 | Marketplace error surfaces | L2 |
| 9 | Discovery collision during silo restart post-install → `/install` returns 504 + Aspire resource state in body | Restart failure surface | L5 |
| 10 | `GET /discovery/table` returns JSON with all canonical + reactive registrations | Debug endpoint | L5 |
| 11 | Cross-silo fire propagates `CorrelationId` + `CausedByEventId` + W3C `TraceParent` into child `NeuronContext` | Causation across silo hops | L3 |
| 12 | Cross-silo fire emits one `fire` producer span + one `handle` consumer span linked via W3C traceparent | OTel correlation | L5 |
| 13 | Alpha `ctx.FireBroadcast<SomethingObserved>` → both `DeltaFirstListener` and `DeltaSecondListener` receive, recorded in `IInoTestCapture` | Fan-out dispatch | L3 |
| 14 | One Delta listener throws; the other still receives; broadcast completes without aggregate failure | Per-listener isolation | L2 |
| 15 | `FireBroadcast` with zero registered listeners completes successfully with zero `CaptureEntry` additions | Broadcast absence semantics | L2 |
| 16 | Two reactive listeners in one bundle (Delta) both register; Discovery returns two `ReactiveTarget` entries for `SomethingObserved` | Multi-grain bundle registration | L2 |

Scenarios 2, 3, 4, 5, 11, 12, 13 are the spine — if any regresses, Phase 2's thesis is broken.

### 12.6 `InoMultiSiloFixture`

Phase 1's `InoTestSiloFixture` is single-silo. L3 tests need two silos in one cluster. `Ino.Testing.InoMultiSiloFixture` composes two `TestCluster`s sharing a cluster id, with per-silo silo configurators:

```csharp
public sealed class InoMultiSiloFixture : IAsyncLifetime
{
    public TestCluster SystemSilo { get; private set; } = null!;
    public TestCluster ExperiencesSilo { get; private set; } = null!;
    // identity silo deferred to Phase 5 — fixture optional override hook

    public async ValueTask InitializeAsync() { /* two-cluster build, shared cluster id */ }
}
```

Fallback if `TestCluster` can't share cluster id across instances: single `TestCluster` with two silo configurators, differentiated by service tag. Verified via Context7 during implementation.

### 12.7 `InoTestAppHost` — L5 fixture

Wraps `DistributedApplicationTestingBuilder` with environment-variable overrides for `InoPaths.InstalledJson` so each test class gets an isolated state file. One `ICollectionFixture<InoTestAppHost>` per L5 test project; ~30s cold start paid once.

## 13. PR #9 findings folded in

| # | Fix | Scope |
|---|---|---|
| I1 | `Capability.Http.AllowedHosts` / `Capability.Identity.Scopes` → `ImmutableArray<string>`; normalize null to empty in constructor | `Ino.Core.Capability` |
| I2 | `NeuronResult.TryGetPayload<T>([MaybeNullWhen(false)] out T? payload)` | `Ino.Core.NeuronResult` |
| I4 | New test asserting causation envelope field mapping in `Neuron<TEvent>.RaiseAsync` | `Ino.Core.Hosting.Tests` |
| I5 | `FindEventAsync` branch coverage: hit, miss, empty id, null id | `Ino.Core.Hosting.Tests` |
| I6 | `NeuronContext` becomes `sealed record` | `Ino.Core.Hosting.NeuronContext` (already baked into §8) |
| I10 | Doc string sweep: replace all `Neuron<TState, TEvent>` references with `Neuron<TEvent>`; update `Ino.Core.Hosting.csproj` description | `Ino.Core.Hosting` docs |

Deferred: I3, I7, I8, I9, I11, M1-M9 → dedicated cleanup branch after Phase 2 lands.

## 14. Project layout

Adds to the POC solution (Phase 1 projects unchanged except PR #9 I1, I2 touch-ups):

```
D:\ino\POC\
├── src/
│   ├── Ino.Core/                           (Phase 1, extended for I1 + I2)
│   ├── Ino.Core.Hosting/                   (Phase 1, extended with typed identity, IExperience,
│   │                                        NeuronContext sealed record, IFirePort, IAmbientFire)
│   ├── Ino.Testing/                        (Phase 1, extended with InoMultiSiloFixture,
│   │                                        InoTestAppHost, IInoTestCapture, NeuronContextForTest)
│   │
│   ├── Ino.Aspire.Hosting/               NEW  AddIno + WithExperience<T>() + IInoBuilder + InstalledSet
│   │
│   ├── Ino.System/                       NEW  Discovery grain, SystemEcho, marketplace controller,
│   │                                          ExperienceRestartService, system silo wiring
│   ├── Ino.System.Contracts/             NEW  EchoRequest, EchoResponse + MarketplaceFeed/InstalledState records
│   │
│   ├── Ino.Identity/                     NEW  stub silo
│   │
│   ├── Ino.Experiences/                  NEW  experiences silo wiring (startup registration, capability-map build)
│   │
│   ├── Ino.System.Host/                  NEW  Orleans silo + ASP.NET host for marketplace HTTP
│   ├── Ino.Identity.Host/                NEW  Orleans silo host
│   ├── Ino.Experiences.Host/             NEW  Orleans silo host
│   └── Ino.AppHost/                      NEW  Aspire DistributedApplication entrypoint
│
├── experiences/
│   └── testing/                          NEW  four test-fixture experiences
│       ├── Ino.Testing.Fixture.Alpha/    + Ino.Testing.Fixture.Alpha.Contracts/
│       ├── Ino.Testing.Fixture.Beta/     + Ino.Testing.Fixture.Beta.Contracts/
│       ├── Ino.Testing.Fixture.Gamma/    + Ino.Testing.Fixture.Gamma.Contracts/
│       └── Ino.Testing.Fixture.Delta/    + Ino.Testing.Fixture.Delta.Contracts/
│
└── test/
    ├── Ino.Core.Tests/                     (Phase 1, extended for I1 Capability + I2 TryGetPayload)
    ├── Ino.Core.Hosting.Tests/             (Phase 1, extended for I4 causation + I5 FindEventAsync)
    ├── Ino.Testing.Tests/                  (Phase 1, unchanged)
    │
    ├── Ino.System.Tests/                 NEW  L2 — Discovery, marketplace controller
    ├── Ino.Experiences.Tests/            NEW  L2 — FirePort, capability enforcer, IAmbientFire
    ├── Ino.Hosting.Tests/                NEW  L3 — InoMultiSiloFixture multi-silo dispatch
    └── Ino.E2E.Tests/                    NEW  L5 — DistributedApplicationTestingBuilder + install flow
```

## 15. Risks to verify via Context7 during implementation

1. **Orleans 10 multi-silo single-cluster topology** — confirm `UseLocalhostClustering` lets three silo processes register as one cluster; confirm placement directors route cross-silo grain calls transparently; confirm `GetGrain(grainClassNamePrefix: ...)` targets the correct silo when only one silo hosts the grain type.
2. **Aspire `ResourceCommandService` surface** — confirm namespace, DI registration in an ASP.NET host inside an Aspire resource, behavior when the restarting resource enters `Failed` state (timeout vs exception), interaction with `ResourceNotificationService.WaitForResourceHealthyAsync`.
3. **`DistributedApplicationTestingBuilder`** — confirm multi-silo cold-start time, environment-variable override flow for `InoPaths.InstalledJson`, graceful shutdown across three silo processes.
4. **xunit.v3 multi-silo fixture lifecycle** — verify `InoMultiSiloFixture` composing two `TestCluster`s within a single `ICollectionFixture<T>`; fall back to two `TestCluster`s in one cluster-id if direct composition is infeasible.
5. **Orleans `JsonConverter<BundleId>`** — confirm System.Text.Json custom converter integrates cleanly with Orleans' `[GenerateSerializer]` on records carrying `BundleId` fields.

Each verification runs before the implementation work that depends on it. Context7 `resolve-library-id` + `get-library-docs` calls land in the implementation plan as explicit tasks.

## 16. Relationship to the broader phase plan

Phase 2 is load-bearing for every later phase:

- **Phase 3** (analyzer + source generator) fills in `IExperience.PerGrainCapabilities` automatically, replaces manual-declaration ceremony. Does not change Phase 2's runtime shape.
- **Phase 4** (Notes + memory search) adds `ctx.Search` facade to `NeuronContext` and ships `SearchIndexer`/`SearchQuery` neurons in the `system` silo. Requires Phase 2's cross-silo routing and Discovery.
- **Phase 5** (Travel + identity + marketplace consent) fills in the real `identity` silo (replaces stub) and turns the 501 consent endpoint into real two-step install. Requires Phase 2's marketplace scaffold and identity silo placeholder.
- **Phase 6** (Playback + CausationIndex + BranchManager) uses the causation envelope propagation Phase 2 already exercises via scenarios 2 and 11.

No later phase needs to retrofit Phase 2's shapes — every extension point is reserved in the interfaces that ship here.
