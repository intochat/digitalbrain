# DigitalBrain v2 — The Neuron Substrate

**Status:** Ratified in brainstorm 2026-09-02; written-spec review pending
**Date:** 2026-09-02
**Supersedes:**
- [2026-08-23-smart-prompt-execution-architecture-design.md](./2026-08-23-smart-prompt-execution-architecture-design.md) — entirely. Smart Prompts are retired; the Execution Neuron is narrowed into the Activity Neuron; `DigitalBrain.Scripting`'s "out-of-process, never loaded in Kernel" position is reversed.
- [2026-08-22-type-safe-behavior-event-architecture-design.md](./2026-08-22-type-safe-behavior-event-architecture-design.md) — the Behavior model.
- [2026-08-25-reqnroll-behavior-runtime-design.md](./2026-08-25-reqnroll-behavior-runtime-design.md) — the behavior interpreter.
- The Smart Prompt section of [ARCHITECTURE.md](../../ARCHITECTURE.md), and its "English + binding chips, not generated C#" position.
**Design companion:** [Interactive walkthrough](https://claude.ai/code/artifact/aed88ac5-68f1-498d-8d13-e503613569db) — three step-through graph scenarios.

---

## 1. Decision summary

v1 spends one word — `Synapse` — on two different things: the message that travels, and the
connection it travels along. Every capability this design adds (learned routing, decay, similarity
search over connections, pruning dead paths) is behaviour of the **connection**. v2 separates them.

> **The graph is anatomy: durable, weighted, slowly changing.
> A firing is physiology: transient, traced, gone.**

Everything else follows from that split.

### Decisions ratified

| # | Decision | Rationale |
|---|---|---|
| D1 | `Synapse` (message) → **`Signal`**. `Synapse` becomes the **edge**. | One word cannot carry both. The edge is the architecture; the message is the boring part. |
| D2 | No `IAggregate` type. `Neuron<TState>` **composes** a plain POCO state object. | Domain logic unit-testable with no silo. "Aggregate" stays a documentation word. |
| D3 | `IDigitalBrain` is a **zero-logic facade** over a root neuron grain. | The console needs `await using`; a grain can't be one. All behaviour stays on the graph. |
| D4 | **Four traffic planes**, kept separate: Signal, Internal event, Broadcast, Direct call. | The 512-entry traffic journal would be evicted by its own weight bookkeeping. |
| D5 | Packages: `DigitalBrain.Contracts` → `DigitalBrain` → `DigitalBrain.Silo`. | `DigitalBrain` is **module one** — the reference shape every other module aligns to. |
| D6 | Day-one console proves a **weighted, durable graph**, not just message passing. | If the console can't print a weighted graph, v2's central claim is untested. |
| D7 | A synapse is **not a grain**. It lives in the source neuron's durable state. | Grain-per-edge does not survive the first million edges. |
| D8 | **Activity is first-class**, one per correlation, not per operation. | Needs progress, cancellation, and an embedding. Grain-per-request is normal Orleans load. |
| D9 | Handler table: **source generator**, but not in slice one. | Buys startup, trimming, and the compile-time tier-1 index. Slice one keeps cached reflection. |
| D10 | Blocking receivers: **membrane enforces, synapse carries the flag, innate only**. | One enforcement point. A discovered synapse can never gain veto power. |
| D11 | **One authoring surface: generated C#.** Smart Prompts retired. | English is how you ask; a compiled neuron is what you get. No second runtime. |
| D12 | Activation pipeline: eShop's `ActivationHandler<T>` shape, **all-must-run** not first-match-wins. | Every warm-up step is required; none claims exclusivity. |
| D13 | Slice one **compiles nothing**. The Roslyn/ALC chain is slice two. | Isolates a routing bug from a load-context bug. |
| D14 | **Typed effectors.** `IShellNeuron` exists but is **not in the default grant**. | "Run any command" is every capability at once; it would make capability-freedom a fiction. |
| D15 | `UserCorrected` recorded from **day one**, model years later. | Training data cannot be reconstructed after the fact. |
| D16 | Flutter: **activity broadcasts for liveness, journal watch for detail.** No kernel-side navigation. | A brain with one cursor is wrong for concurrent activities. |
| D17 | **Rename in place.** No parallel `v2` namespace. | `DigitalBrain.v2.Core` forces every module to pick a side and carries both for months. |
| D18 | Generated code loads **in-silo, in a collectible ALC** — reversing the prior out-of-process decision. | Safety comes from capability-freedom (§9.3), not from process isolation. Out-of-process cannot be a graph citizen. |

### Non-goals

WASM sandboxing. A trained routing model. Marketplace signing and economics. Multi-tenant
graph partitioning beyond the existing `OwnerId`. A visual flow editor. These are named so they
are not smuggled in.

---

## 2. Vocabulary

| Term | Is | Is not |
|---|---|---|
| **Neuron** | The actor. A journaled Orleans grain; sole writer of its own state. | A message handler bag. |
| **Synapse** | A directed, typed, weighted edge between two neurons. | A message. A grain. |
| **Signal** | An immutable record crossing a synapse. Carries correlation + causation. | A state change. |
| **Entity** | A passive durable grain, off the graph, read directly by clients. | A graph endpoint. |
| **Activity** | The durable trace of one correlation: progress, participants, status. | A journal. |
| **Effector** | A neuron that performs a side effect on the outside world. | Ambient capability. |
| **Sensor** | A neuron that turns an external event into a typed signal. | An HTTP listener. |

The sentence that decides D1: *a neuron fires a signal along a synapse.*

---

## 3. Citizens

| Citizen | Durable | On graph | Journaled | Written by | Read by |
|---|---|---|---|---|---|
| Neuron | yes | yes — endpoint | in + out | itself only | nobody directly |
| Synapse | yes (in source neuron) | yes — edge | no | its source neuron | graph queries, UI, vector index |
| Entity | yes | no | no | a neuron, on its turn | clients + UI, directly |
| Activity | yes | no — a trace of it | n/a | the runtime | UI, agents, search |

### 3.1 Entities, restated

`docs/JOURNALS.md` rule 1 stands unchanged: **neurons own traffic journals, entities own snapshots,
memory owns history.** A UI button is an entity — small persistent state, no synapses, no journal,
read directly by Flutter, mutated only when a neuron handles the signal its press produced.

**Binary payloads never live in grain state.** An `ImageEntity` holds a *reference*: blob URI,
content hash, MIME type, dimensions, caption, and the embedding vector. Grain state is replicated
and compacted; megabyte payloads there are a durability and memory defect. The entity is what the
vector index points at, which is why the metadata sits with the reference rather than the bytes.

---

## 4. Contracts

`DigitalBrain.Contracts` — `Microsoft.Orleans.Sdk` only. No journaling, no server.

```csharp
public abstract record Signal;
public abstract record Signal<TResponse> : Signal where TResponse : Signal;

public interface IHandle<in TSignal> where TSignal : Signal
{
    Task HandleAsync(TSignal signal, CancellationToken cancellationToken);
}

public interface INeuron : IGrainWithStringKey
{
    Task Deliver(SignalDelivery delivery, CancellationToken cancellationToken = default);

    [ReadOnly, AlwaysInterleave]
    Task<JournalRead> ReadJournal(JournalKind kind, long afterSequence);

    [ReadOnly, AlwaysInterleave]
    Task<IReadOnlyList<Synapse>> ReadSynapses();

    Task Watch(JournalKind kind, long afterSequence, IJournalObserver observer);
    Task Unwatch(IJournalObserver observer);
}

// Compile-time metadata. Feeds tier-1 routing and the discovery embedding.
public interface INeuronDescriptor
{
    static abstract string Description { get; }
}
```

`SignalDelivery` is v1's `SynapseDelivery` renamed — no shape change. `NeuronId`, `OwnerId`,
`CorrelationId`, `SignalId` (was `SynapseId`), `JournalKind`, `JournalRead`, `IJournalObserver`
carry over verbatim.

### 4.1 The synapse

```csharp
public enum SynapseKind { Innate, Learned, Discovered }

public readonly record struct Synapse(
    NeuronId Source,
    NeuronId Target,
    string SignalType,
    double Weight,
    DateTimeOffset LastFiredAt,
    SynapseKind Kind,
    bool IsBlocking)
{
    public double WeightAt(DateTimeOffset now, double halfLife) =>
        Weight * Math.Pow(0.5, (now - LastFiredAt) / halfLife);

    public bool IsPruned(DateTimeOffset now, double halfLife, double floor) =>
        Kind != SynapseKind.Innate && WeightAt(now, halfLife) < floor;
}
```

**Invariant:** `IsBlocking` implies `Kind == Innate`. Enforced at construction.

### 4.2 The facade

```csharp
public interface IDigitalBrain : IAsyncDisposable
{
    OwnerId Owner { get; }

    NeuronReference<TNeuron> Get<TNeuron>(string name = "default") where TNeuron : INeuron;
    TEntity GetEntity<TEntity>(string name = "default") where TEntity : class, IEntity;

    Task FireAsync<TNeuron>(string name, Signal signal, CancellationToken ct = default)
        where TNeuron : INeuron;

    Task<IReadOnlyList<Synapse>> GetSynapsesAsync(CancellationToken ct = default);
    Task<JournalRead> ReadJournalAsync(NeuronId subject, JournalKind kind, long after = 0, CancellationToken ct = default);
    IAsyncEnumerable<JournalRead> WatchJournalAsync(NeuronId subject, JournalKind kind, long after = 0, CancellationToken ct = default);
}
```

Every method is a one-liner onto the root neuron grain (v1's `SessionNeuron`, promoted and renamed
`BrainNeuron`). The facade holds no logic and no state beyond the Orleans client. D3.

---

## 5. Routing

Three tiers assemble the receiver set, in order. Capability is **declared** in types, **remembered**
in synapses, and only **searched for** when the first two come up empty.

| Tier | Source | Cost | Can be wrong |
|---|---|---|---|
| 1 — Innate | who declares `IHandle<T>` (compile-time index) | free | no |
| 2 — Learned | the neuron's own synapse set, read on activation | in-memory | no |
| 3 — Discovered | similarity search over `INeuronDescriptor.Description` | one embedding lookup | **yes** |

Tier 3 runs **only on a miss**, and its result is written back as a `Discovered` synapse at
weight 0.10 that must earn its place through use.

### 5.1 Constraints on tier 3

Non-deterministic routing is the single largest risk in this design. Three constraints contain it:

1. Tier 3 never fires *instead of* tiers 1–2 — only in addition, and only when they returned nothing.
2. Tier 3 can never produce a **blocking** receiver (D10).
3. `RoutingOptions.StrictMode` disables tier 3 entirely. **All tests run in strict mode by default**;
   discovery is exercised only by tests that name it.

### 5.2 Ranking

```csharp
public interface ISynapseRanker
{
    IReadOnlyList<Synapse> Rank(IReadOnlyList<Synapse> candidates, RoutingContext context);
}
```

The only seam ML ever touches. Default implementation sorts by `WeightAt(now)`. A trained ranker
replaces it later and **may only reorder — never add or remove**. Both implementations therefore
produce the same *set*, which keeps every test deterministic and every incident explainable.

---

## 6. Synapse mechanics

- **Potentiation** — a firing the receiver *handled* raises `Weight` and stamps `LastFiredAt`. An
  unhandled signal does not.
- **Decay** — computed at read: `weight × 0.5^(Δt / halfLife)`. **No timers.** A synapse nobody uses
  is already weak the next time anyone looks.
- **Pruning** — one Orleans reminder **per neuron** (never per synapse) sweeps its own set on a slow
  cadence, purely to reclaim storage.
- **Innate synapses never decay.** They derive from `IHandle<T>` declarations and are as durable as
  the code.

Rejected: a reminder per edge. One reminder per synapse does not survive the first thousand neurons,
and buys nothing that read-time arithmetic doesn't.

---

## 7. Traffic planes

| Plane | Shape | Storage | Retention | Contract |
|---|---|---|---|---|
| Signal | directed, along a synapse | traffic journal, both ends | 512 entries | public |
| Internal event | self only | Orleans.Journaling state | compacted, unbounded | internal |
| Broadcast | undirected fan-out | `BroadcastChannel` | none, at-most-once | public |
| Direct call | request/response | nothing | n/a | public |

**Hard rule:** a neuron may never declare `IHandle<T>` for its own internal event. Weight bookkeeping
fires far more often than real traffic; routed through the bounded traffic journal it would destroy
the observable record with its own accounting.

---

## 8. The membrane and the activation pipeline

### 8.1 Membrane

An `IIncomingGrainCallFilter` wrapping `Deliver`. It is the only enforcement point for:
authorization (re-entering `VerifiedActor` from `delivery.Principal`, as v1 already does), blocking
receiver consultation, rate limiting, activity enrollment, and tracing. Nothing bypasses it, and
there is exactly one of it. D10.

### 8.2 Activation pipeline

eShop's `ActivationHandler<T>` shape — `CanHandle(args)` + `HandleAsync(args)`, DI-ordered — with
**inverted semantics**: eShop's chain is first-match-wins because one handler claims a launch;
neuron activation is all-must-run because every step is required. D12.

Ordered steps: restore durable state → load synapse set → subscribe to broadcasts → register
reminders → build context.

`Neuron` **holds** an `INeuronActivationPipeline` (composition, not inheritance). `OnGrainActivated`
is never exposed and never overridden. Modules register their own handlers via DI, which is how the
AI module adds "load the agent's toolset" without touching the kernel.

---

## 9. Sensors, effectors, and scripting

### 9.1 Afferent and efferent

- **Sensor (afferent)** — external event → typed signal. Webhooks, reminders, MCP tool calls, SSE.
  The only place untyped data enters the system.
- **Effector (efferent)** — signal → external effect. Git, dotnet, shell, HTTP, file system.
- **Interneuron** — internal only. Assistant, compliance, memory, router.

### 9.2 The webhook gateway

**A grain cannot own an ASP.NET pipeline.** Grains migrate between silos, deactivate when idle, and
exist as multiple activations over their lifetime; an HTTP listener does none of that.

The split: the **neuron owns the route declaration** as durable state; the **silo owns one gateway**
that rebuilds its `EndpointDataSource` from those declarations. A neuron created at 03:00 is
reachable at 03:00 with no redeploy. The neuron's only job on the request path is translating a
foreign payload into a typed signal.

### 9.3 A script is a neuron

Deterministic flows need no second runtime. **Sequencing is `await`. Parallelism is `Task.WhenAll`.
The trigger is `IHandle<T>`. Discovery is `static Description`.**

```csharp
public sealed class PrVerificationNeuron : Neuron, IHandle<PullRequestOpened>, INeuronDescriptor
{
    public static string Description =>
        "Verifies a pull request: clean, build, test, then three parallel reviews.";

    public async Task HandleAsync(PullRequestOpened pr, CancellationToken ct)
    {
        await FireAsync<IGitNeuron>(new Clean(pr.Repo), ct);
        await FireAsync<IDotNetNeuron>(new Build(pr.Repo), ct);
        await FireAsync<IDotNetNeuron>(new Test(pr.Repo), ct);

        var reviews = await Task.WhenAll(
            FireAsync<IReviewAgent>("quality",      new Review(pr.Diff), ct),
            FireAsync<IReviewAgent>("architecture", new Review(pr.Diff), ct),
            FireAsync<IReviewAgent>("tests",        new Review(pr.Diff), ct));

        await EmitAsync(new PrVerdict(pr.Number, reviews));
    }
}
```

Three properties make this safe:

**Genotype / phenotype.** The **source** is the durable artifact — persisted, hashed, signed. The
**assembly** is re-created on every silo start by compiling into a collectible `AssemblyLoadContext`.
A collectible context also lets a later version unload the previous one without a silo restart.

**The compiler is the verifier.** Generated code is compiled against module contracts before
anything runs; errors return to the author neuron as an ordinary signal and the repair loop is
ordinary graph traffic. This is the concrete return on "typed C# only".

**Capability-free by construction.** `System.IO`, `System.Diagnostics.Process` and networking are
**not in the compilation's reference set**. A script cannot open a file or spawn a process — it can
only `FireAsync` at effector neurons. Its power is exactly the effectors its owner granted. This,
not process isolation, is why D18 reverses the prior out-of-process decision: an out-of-process
script cannot be a graph citizen, and does not need to be isolated once it holds no capabilities.

**Typed effectors, and `IShellNeuron` is ungranted by default** (D14). A generic shell is every
capability at once and would make the guarantee above a fiction. Granting it is an explicit,
visible, per-owner act.

---

## 10. Learning

| Loop | Speed | Learns from | Changes | Technology |
|---|---|---|---|---|
| Hebbian | every fire | the receiver handled it | synapse weight | arithmetic |
| Corrective | every correction | `UserCorrected` | weights + a durable preference | embeddings (MEAI) |
| Predictive | nightly, offline | journals + corrections as labelled pairs | the *order* of the receiver set | ML.NET ranker |

```csharp
public sealed record UserCorrected(
    CorrelationId Turn,
    string Wanted,
    NeuronId? InsteadOf) : Signal;
```

Handled immediately: depotentiate the synapse that produced the rejected path; potentiate the named
alternative; write a durable preference with an embedding. **Recorded from slice one** even though
no model exists for a long time — D15, because the table cannot be backfilled.

Embeddings come from `Microsoft.Extensions.AI`'s `IEmbeddingGenerator`, not ML.NET. The Ollama
`IEmbeddingGemma` marker already wired for offline dev is the slice-one implementation, and
`ARCHITECTURE.md`'s config-driven dimension rule stands (Qdrant index dims lock to it).

---

## 11. Packages and file layout

```
DigitalBrain.Contracts   (was DigitalBrain.Abstractions)  — Orleans.Sdk only
        ↓
DigitalBrain             (was DigitalBrain.Core)          — module one, the reference shape
        ↓
DigitalBrain.Silo        (was DigitalBrain.Kernel)        — hosting, gateway, filters
```

`DigitalBrain` is **module one**: the module every other module aligns to, and the only one the
day-one console references. The reference direction never reverses — a module that only declares a
signal type does not drag in `Orleans.Journaling` or the server.

`DigitalBrain.Core` is retired as a package name. **Verified safe:** version is `0.1.0-alpha.1`
and no publish step exists in any workflow, so nothing has shipped. The rename costs nothing now
and would cost a deprecation shim later — which is an argument for doing it before the first push.

### 11.1 Files

`src/Kernel/DigitalBrain.Contracts/`
```
Neurons/INeuron.cs          Neurons/IHandle.cs           Neurons/INeuronDescriptor.cs
Signals/Signal.cs           Signals/SignalDelivery.cs
Synapses/Synapse.cs         Synapses/SynapseKind.cs
Journals/…  Identity/…  Entities/IEntity.cs              (carried over from Abstractions)
```

`src/Kernel/DigitalBrain/`
```
Neuron/Neuron.cs                    — base grain, dispatch, journals
Neuron/SynapseSet.cs                — the routing table: load, potentiate, decay, prune
Neuron/SignalRouter.cs              — the three tiers
Neuron/ActivationPipeline.cs        — the all-must-run chain
Neuron/BrainNeuron.cs               — root neuron (was SessionNeuron)
Activity/ActivityNeuron.cs          — narrowed from Modules/Execution
Entities/Entity.cs                  — unchanged from v1
Hosting/DigitalBrainFacade.cs       — IDigitalBrain
```

`src/Kernel/DigitalBrain.Core/v2/CoreV2.cs` is **deleted**. Nothing in it survives verbatim:
`Synapse` changed meaning, `IAggregate` is gone, and the file does not currently compile.

---

## 12. Migration

**Rename in place. No parallel `v2` namespace** (D17) — it would force every module to pick a side
and carry both for months. The rename is mechanical and the compiler finds every site.

| Step | Change | Blast radius |
|---|---|---|
| M1 | `Synapse` → `Signal`, `SynapseDelivery` → `SignalDelivery`, `SynapseId` → `SignalId` | ~40 files in Abstractions + all modules |
| M2 | `DigitalBrain.Abstractions` → `.Contracts`; `.Core` → `DigitalBrain`; `.Kernel` → `.Silo` | csproj, slnx, Aspire manifests, all references |
| M3 | Delete `Modules/SmartPrompt` (33 files, ~3,000 LOC) + 6 test files + `Mcp/SmartPromptTools.cs` + AppHost wiring | listed in §12.1 |
| M4 | Narrow `Modules/Execution` → `Activity`: drop `WorkloadDescriptor` and the executor indirection, keep the progress/cancellation spine | ~1,160 LOC in, less out |
| M5 | Retire 8 SmartPrompt design docs; update `ARCHITECTURE.md` and `JOURNALS.md` vocabulary | docs only |
| M6 | Delete `src/Kernel/DigitalBrain.Core/v2/CoreV2.cs` | 1 file |

### 12.1 SmartPrompt removal sites

`DigitalBrain.slnx` · `Aspire/DigitalBrain.AppHost/AppHost.cs` + `.csproj` ·
`Kernel/DigitalBrain.Kernel/MapBehaviors.cs` + `.csproj` · `Kernel/DigitalBrain.Mcp/`
(`SmartPromptTools.cs`, `McpSurface.cs`, `Program.cs`, `.csproj`) ·
`Modules/Execution/Contracts/WorkloadDescriptor.cs` · `Modules/Execution/Execution/ExecutionNeuron.cs` ·
`Modules/Salesforce/Salesforce/McpSalesforce.cs` · `tests/DigitalBrain.Aspire.Tests/` (2 files) ·
`tests/DigitalBrain.E2E.Tests/` (3 files) · `tests/DigitalBrain.Simulation.Tests/SmartPrompt/` (6 files)
+ `Execution/` (2 files).

`DigitalBrain.Scripting` (currently a 4-line stub) is repurposed in slice two as the Roslyn +
collectible-ALC host, in-silo. Its prior "never loaded in Kernel" charter is void (D18).

---

## 13. Slices

### Slice 1 — the graph is real *(no compilation, no AI, no modules)*

`DigitalBrainConsole` references `DigitalBrain` + `DigitalBrain.Silo` and nothing else. Handlers are
`Console.WriteLine`, in the spirit of `D:\ModernCQRS`.

```
[greeter] handled UserMessageReceived("hello")       -> "Hello!"
[greeter] handled UserMessageReceived("hello again") -> "Hello!"

-- synapses (anatomy) ------------------------------------------
chat:main --UserMessageReceived--> greeter:default   w=0.72  fired=2  learned
chat:main --UserMessageReceived--> logger:default    w=1.00  fired=2  innate

-- chat:main outgoing journal (physiology) ---------------------
#1  UserMessageReceived  corr=a41f…  cause=—      -> 2 receivers
#2  UserMessageReceived  corr=b903…  cause=—      -> 2 receivers
```

Four falsifiable claims: neurons dispatch by type without the sender naming the receiver; synapses
are durable and print with source/target/type/weight; `w=0.72` after two fires proves potentiation
and survives restart; anatomy and physiology are two lists with two retention policies.

### Slice 2 — the compile chain

The vertical your survey names as the gap no tree has closed: generate C# → compile against module
contracts → collectible ALC load → register grain type → fire a signal it handles → assert the
journal shows its response → unload on new version. Harvests v4's `CollectibleAssemblyLoadContext`
and v3's Gate flow.

### Slice 3 — sensors and effectors

Webhook gateway with runtime route declarations; typed `IGitNeuron` / `IDotNetNeuron`; the PR
verification scenario end to end.

### Slice 4 — discovery and corrections

Tier-3 similarity search behind `StrictMode`; `UserCorrected`; embeddings via MEAI.

**Slice ordering rationale (D13):** if the synapse model is wrong, slice 1 reveals it in a day. Bundle
the ALC work in and a routing bug is indistinguishable from a load-context bug for a week.

---

## 14. Testing

- **Tier 1 — unit, no silo.** `Neuron<TState>` composes a plain POCO (D2), so folding and invariants
  test with no Orleans. `Synapse.WeightAt` and `IsPruned` are pure functions.
- **Tier 2 — simulation.** The existing `DigitalBrain.Testing` cluster suite covers routing, journal
  resume, reset-snapshot at the retention boundary, potentiation across restart, and pruning.
- **Strict mode is the default** in every test (§5.1). Discovery is exercised only by tests that
  name it, so no test is non-deterministic by accident.
- **Slice 2** gets its own end-to-end chain test, using `final`'s distribution harness as the
  template and v3/v4's compile+ALC assertions.

---

## 15. Risks

| Risk | Severity | Mitigation |
|---|---|---|
| Tier-3 routing is non-deterministic | high | Strict mode by default; never blocking; never replaces tiers 1–2 |
| Generated code executes in-silo | high | Capability-free compilation set; typed effectors; shell ungranted (§9.3) |
| `Synapse`→`Signal` rename touches every module | medium | Mechanical; compiler finds every site; one commit, no parallel namespace |
| Synapse set grows unbounded on a hub neuron | medium | Read-time decay + per-neuron prune reminder; cap with lowest-weight eviction |
| Activity grain per correlation under load | low | Deactivates at end of operation; record moves to the owner's activity log |
| Collectible ALC leaks on unload | low | Slice 2 harvests v4's tested unload+GC assertions rather than writing fresh |

---

## 16. Open items for review

1. **Half-life and prune-floor defaults** for synapse decay — needs a number, not a symbol.
2. **Does `Modules/Execution` get narrowed (M4) or rewritten?** The spec assumes narrowed.
3. **Traffic journal cap** stays at 512 entries / 512 KB, or rises now that signals carry routing
   fan-out metadata?
