# DigitalBrain v2 Core — ratified architecture

Date: 2026-08-05. Status: **RATIFIED** for Core shape. Inputs: baseline four-type ABI,
`CORE-DESIGN.md`, `CORE-RESEARCH.md`, `OS.md`, `FLOWS.md`, product traps, Orleans surface
requirements. Method: adversarial grill (15+ attacks), delete-first, one mechanism per job.

This document supersedes `CORE-DESIGN.md` wherever they conflict on **Abstractions thickness**,
**where Core vocabulary lives**, and **Orleans feature placement**. Physics that survived every
review (commit-before-dispatch, no neuron-awaits-neuron, journals as truth, at-least-once +
watermark dedup, string kind addresses) stands.

---

## 1 · Ratified principles

1. **Abstractions are four types.** Anything a module can name without inheriting `Neuron` lives
   in `DigitalBrain.Abstractions`. Everything else is Core or a module. Ceremony in the ABI is a
   bug.
2. **Orleans is Core's body, not the ABI.** Reentrancy, filters, RequestContext, DurableGrain /
   journaling, reminders, timers, placement, streams (edge only), versioning seams — all live
   *inside* `DigitalBrain.Core`. Modules never import Orleans.
3. **One causal bus.** Neuron→neuron delivery is journaled outbox + direct grain `Deliver`.
   Streams, pub-sub products, and registries are never the authoritative path for facts between
   neurons.
4. **Nothing leaves before commit.** Return of delivery means *committed*, never the answer.
   Neurons never await neurons. Continuations are later turns.
5. **Causation is structure, not an author API.** Public `SynapseMetadata` is identity only
   (source, sequence, timestamp). Cause / answers / receiver snapshots live on **Core journal
   entries**. Modules do not stamp lineage.
6. **Declarations define capability; connections define instance wiring.** Same-context fan-out
   from `INeuron<T>` declarations; cross-context and instance routes from journaled
   `Connect`/`Disconnect` on the emitter. No dual bus. No in-turn remote registry.
7. **Core owns all durable mutation in a turn.** One batch commit. Module-visible
   `WriteStateAsync`, raw `IDurable*`, `GrainFactory`, extra grain interfaces, `IRemindable` are
   sealed away.
8. **Prefer delete.** Two mechanisms for one job → keep one. A type without a consumer today is
   not shipped. Kernel (behavior lifecycle, capability gates) is not Core.
9. **Never clog.** No reentrancy deadlock class, no god facade, no emit-path lookup with timeout
   retracting the turn, no `System.Type`/AQN in durable addresses, no dual derivation of topology.
10. **Proof is live or journaled.** No synthetic observations. Terminal failures are journaled.
    Boot refusals are tested contracts.

---

## 2 · Thin Abstractions (exactly four types)

Package: `DigitalBrain.Abstractions`. Namespace: `DigitalBrain`. **Zero dependencies.**

```csharp
namespace DigitalBrain;

public abstract record Synapse;

public interface INeuron<in TSynapse>
    where TSynapse : Synapse
{
    Task HandleAsync(TSynapse synapse, CancellationToken cancellationToken);
}

public readonly record struct NeuronId(string Kind, string Name)
{
    public static string KindOf(Type type) => type.Name.ToLowerInvariant();
    public override string ToString() => $"{Kind}/{Name}";
}

public sealed record SynapseMetadata(
    NeuronId Source,
    long Sequence,
    DateTimeOffset Timestamp);
```

### What each type is for

| Type | Job |
|---|---|
| `Synapse` | Immutable fact body. Modules own sealed records under this root. |
| `INeuron<TSynapse>` | Declaration = subscription = address surface. Hearing is the behavior. |
| `NeuronId` | Durable address: logical kind + context name (locus). Never `System.Type`. |
| `SynapseMetadata` | Transport/read identity of a fact: who said it, at which sequence, when. |

### FORBIDDEN to add to Abstractions

| Candidate | Why banned from Abstractions |
|---|---|
| `Synapse<TReply>` | Reply pairing is a Core ask protocol, not a fact-root fork. |
| Second `INeuron<TQ,TR>` | Dual interfaces force dual dispatch paths into the ABI. |
| `Answer<,>` / `Answered<,>` | Dispatch views are Core-only; never journaled; not module vocabulary. |
| `SynapseRef` | Journal identity structure; Core public or internal, not ABI. |
| `Cause` / `Answers` / `CorrelationId` on metadata | Causation is journal structure; correlation-as-API returns deadlocks and dual truth. |
| `CoreSynapses` pack (`Connect`, `DeliveryFailed`, …) | Core's own vocabulary ships with Core. Modules that listen reference Core. |
| `JournalFact`, `Delivery`, `NeuronReading` | Read models of the runtime; Core edge surface. |
| `IDigitalBrain`, `IModule`, `IBrain`, session interfaces | Facades and composition live in Core/hosting. |
| Orleans attributes, grain interfaces, serializers | Abstractions stay host-agnostic. |
| OwnerId, multi-tenant keys, capability tokens | Kernel / product concerns. |
| Streams, topics, wire names, `[WireTo]` | String routing graveyard. |
| Any "helper" that exists for one sample | Samples are not ABI consumers. |

**Rule:** if a type is only meaningful after a turn commits or only produced by Core, it is not an
Abstraction.

### Where the "missing" types live (not deleted — relocated)

| Concern | Home |
|---|---|
| Ask / answer typing, open-ask pins, `AskAsync` | Core (`IAnswers<,>`, `Neuron` verbs, `Session`) |
| `Connect` / `Disconnect` / `DeliveryFailed` / `Schedule` / … | Core public synapses (closed, append-only) |
| Journal entry Cause / Answers / To / Via | Core `JournalEntry` (durable schema) |
| Public journal read shape | Core `JournalFact` / `NeuronReading` on `Brain.ReadAsync` |
| Continuation sugar (optional) | Core-only dispatch view if a consumer forces it; default = bare reply + TState |

---

## 3 · Core runtime surface (Orleans features map)

Package: `DigitalBrain.Core`. Modules subclass `Neuron` / `Neuron<TState>` and may declare
`IAnswers<,>`. Modules **never** take Orleans types in public module contracts.

### Feature → scenario class (one job each)

| Orleans feature | Serves | Does **not** serve |
|---|---|---|
| **`DurableGrain` + Orleans.Journaling** | Per-neuron journal, outbox-is-journal, watermark, connections table, schedule table, module `TState` slot — one batch commit | Cross-grain transactions; free-form app state |
| **Serialized turns / reentrancy controls** | Single-threaded turn physics; forbids handler re-entry that deadlocks Drain→Deliver | Multi-threaded handlers inside one neuron |
| **`[AlwaysInterleave]` (surgical)** | Committed journal *reads* and health probes that must not queue behind a long model turn | `Deliver`, drain, schedule ticks, any mutating path |
| **Incoming / outgoing grain call filters** | Envelope headers (kind strings only), interface whitelist, self-proxy deadlock → loud fail | Business auth policy (Kernel later); dual bus |
| **`RequestContext`** | Transport convenience for envelope on the wire | Source of truth (does not survive redelivery/storage) |
| **Grain timers** | Fast outbox drain; in-activation schedule ticks | Cross-silo durability alone (reminder is backstop) |
| **Reminders** | Idle-neuron outbox wakeup; schedule backstop | High-frequency pulse (timer + fact is enough) |
| **Direct grain calls (`Deliver`)** | The sole neuron↔neuron delivery mechanism after commit | Fire-and-forget without journal (forbidden) |
| **Streams — explicit** | Edge projections only: UI push, telemetry mirrors, high-volume *ingress adapters* that immediately journal | Authoritative n2n bus; late-join causal history |
| **Streams — implicit subscriptions** | Optional **ingress** auto-activation into a Core edge adapter grain that journals first | Declaration-is-subscription for neurons (catalog owns that) |
| **Placement strategies** | Prefer local for hot session contexts; fixed/system placement for well-known kinds | Load-balance stateful neurons (destroys journal identity) |
| **Stateless workers** | Pure, non-journaled offload only if ever proven (encode, fan-out mirror) — default **no** | Any neuron with a journal, watermark, or name |
| **Grain versioning / redeploy** | Module blue-green + catalog fingerprint / epoch (Stage 3 Revision lands here) | Per-message version negotiation in Abstractions |
| **Orleans Transactions** | **Non-goal** unless a single scenario proves multi-grain ACID with no fact-saga | Default coordination (facts + TState join already cover it) |
| **Grain services / DI in grain ctor** | Module dependencies (`HttpClient`, model clients) via primary constructor | Module-owned second DI container |

### Concurrency contract (load-bearing)

- `Deliver` and drain are **non-reentrant** on the same activation.
- Drain awaits remote `Deliver` **outside** the emitting turn's handler (post-commit timer /
  reminder turns). Handler must not call back into the neuron that is mid-turn → structural
  absence of the Drain↔Deliver deadlock when physics hold.
- Self-delivery uses **direct method call**, never the grain proxy.
- Outgoing filter converts self-proxy attempts into a loud exception.

### Catalog (boot, local, no registry)

- One reflection pass: `Catalog.Build(neuronTypes)`.
- Kind = `NeuronId.KindOf(type)` (lowercased class name); collisions fail boot.
- Listeners: exact `INeuron<T>` declarations.
- Answerers: exact `IAnswers<TQuestion,TReply>` declarations — **at most one** answerer kind per
  question type (boot fail on 0 for edge-asked types is edge-time terminal; boot fail on 2+).
- Fact records sealed; wildcards banned; reserved Core kinds not implementable as module
  handlers for interception kinds.
- Catalog fingerprint must match across silos; mismatch refuses join.

---

## 4 · Communication model (Emit / Send / Reply / Broadcast / Streams)

### Decision (one bus)

**Causal bus = journaled emissions + direct Orleans `Deliver`.**  
**Broadcast = address resolution mode on that bus, not a second transport.**  
**Streams = edge I/O only.**

```
turn handler stages facts → ONE commit (journal + state + tables)
                         → post-commit drain (timer/reminder)
                         → Deliver(fact, metadata) per receiver (at-least-once)
                         → receiver watermark dedup → handle → commit
```

### Verbs

| Verb | Who | Resolution | Notes |
|---|---|---|---|
| `Emit(fact)` | Neuron (in turn) | Declared `INeuron<T>` at **same context name**, **minus** kinds overridden by connections for that fact kind, **∪** connection targets | Default. Speaker names nobody. Zero receivers = legal (`to: []`). |
| `Reply(fact)` | Neuron (in turn) | Turn source (from Core ambient envelope — not module-visible metadata API) | Directed answer; journaled like any said entry; also fans to same-context overhearers of that fact type if declared |
| `Ask<TReply>(question)` | Neuron (in turn) | Routes to the catalog answerer kind **at asker's context name**; registers open ask | Requires asker can hear `TReply` (declare `INeuron<TReply>` or hold join in `TState`). Announce-only: `Emit(question)` reaches listeners, not the answerer role. |
| `Send(to, fact)` | **Edge session** (Stage 1); in-neuron Send only when a real consumer appears (behavior creator) | Exact `NeuronId` | Used for `Connect` delivery and directed edge speech. Not type-coupled (`Send<TNeuron>` is dead). |
| `Session.AskAsync<TReply>(question)` | Edge | Fire ask once → observe **session journal** until matching reply / `DeliveryFailed` / `AskExpired` | Task is volatile sugar; journal is the ask. |
| `Session.EmitAsync` | Edge | Same as neuron Emit from session neuron | Session **is** a neuron. |

There is **no** separate `Broadcast` API. Emit *is* broadcast under declaration∪connection
resolution. Naming a second verb reintroduces ino's dual fire paths.

### Topology (declaration ∪ connection)

```
receivers(fact) =
    declaredListeners(exact type) @ emitter.Name
      EXCEPT kinds present as connection targets for this factKind
  ∪ connections[factKind]
  // dedup by NeuronId; snapshot into said journal entry with via: declared|connected|ask
```

- **Connections** live on the **emitter** (`IDurableDictionary`), mutated only by Core handling
  `Connect`/`Disconnect` through the ordinary bus → rewiring is journaled and happens-before
  later emissions.
- **Ghost rule:** a connection for fact F to kind K suppresses same-context declared fan-out of F
  to K at that emitter (prevents parallel ghost pipelines at the same name).
- **No registry grain.** No emit-path remote lookup. No timeout retracting a staged emit.
- Connect validation against **local catalog** at handling time; violations → `ConnectionRefused`.

### Ask / answer without bloating Abstractions

```csharp
// Core — not Abstractions
public interface IAnswers<in TQuestion, TReply>
    where TQuestion : Synapse
    where TReply : Synapse
{
    Task<TReply?> HandleAsync(TQuestion question, CancellationToken cancellationToken);
}
```

- Null return **defers**; later `Reply`/`Emit` of `TReply` closes the open ask (Core stamps
  **Answers** on the journal entry, not on public metadata).
- Continuations: declare `INeuron<TReply>` and/or keep join state in `TState`. **No `Answer<,>`
  in Abstractions.** If zero-field continuations become a proven ceremony tax, a Core-only
  dispatch view may be added later — it is not Stage-1 ABI.
- Edge matching keys on journal `Answers` / `DeliveryFailed` / `AskExpired`, never Cause-scanning
  by authors.

### Streams policy (explicit)

| Allowed | Forbidden |
|---|---|
| UI / SSE projection from a renderer neuron or edge adapter | Neuron A "publishes" to stream as sole delivery to neuron B |
| Telemetry / metrics mirrors off committed facts | Global timeline stream as the brain's bus |
| Ingress: external bus → stream → **adapter grain** → first `Deliver` + journal | Implicit stream subscription as substitute for `INeuron<T>` catalog wiring |
| Optional Stage-2: replace edge poll with one-way journal observer (still secondary to journal) | Late-subscribe correctness for causal history (streams do not guarantee it) |

**Late-join:** journals retain (bounded + tallies). New listeners do **not** replay history unless
they read journals. That is intentional: broadcast is live nervous system; recall is journal read
(flow "why").

### Delivery physics (port, don't re-litigate)

- At-least-once; receiver watermark on `(Source, Sequence)`; duplicate = silent success ack.
- Journal **is** the outbox; lazy progress map; per-(sender,receiver) FIFO via blocked targets.
- Bounded retry; permanent failures terminal on attempt 1; `DeliveryFailed` on **sender**.
- Poison activation + reload on commit failure; no v1 retraction machinery.
- Arm wakeup **before** commit if emissions have receivers.

---

## 5 · Module & behavior model

### Module (compiled, Stage 1)

A module is an assembly of:

1. Sealed `Synapse` records (vocabulary),
2. `Neuron` / `Neuron<TState>` classes implementing `INeuron<>` and optionally `IAnswers<,>`,
3. Private helpers and DI-registered services (HTTP, models, stores).

Composition:

```csharp
builder.UseOrleans(silo => silo.AddDigitalBrain(
    typeof(XAccount).Assembly,
    typeof(Chart).Assembly));
```

No `IModule` mega-interface in Core. No plugin registry on the emit path. Discovery = catalog
build over explicit type sets. Heterogeneous silos refused by fingerprint.

**Module rules (boot-enforced):** sealed facts; no `INeuron<abstract>`; `TState` default
constructible + codec-round-trippable; reserved Core kinds not hijacked; no extra grain
interfaces.

### Behavior scripting (Kernel consumes Core; Core stays thin)

A **behavior is a neuron**, not a second runtime.

| Stage | Who | What |
|---|---|---|
| Author | Kernel / BehaviorHost | Owner NL or C# → source artifact (content-addressed) |
| Gate | Kernel | Generated test in collectible ALC; green required |
| Activate | Kernel + Core seam | Register neuron type(s) into catalog epoch; journal lifecycle facts later |
| Run | Core | Ordinary `Neuron` + journals + Emit/Reply/Ask |
| Rewire | Facts | `Connect`/`Disconnect` — brain rewires without redeploy |
| Rollback | Kernel | Deactivate epoch; prior catalog fingerprint |

**Core provides:** catalog fingerprint/epoch hook, identical dispatch for compiled and
script-activated kinds, refusal of kind collision, journal durability.

**Core does not provide:** Roslyn host, NL prompts, approval UI, signed deploy, BDD product
gate. Those are Kernel. Putting them in Core recreates the kitchen-sink base class.

Scripted behaviors **must** program *on* neurons/synapses (implement `INeuron`/`IAnswers`, emit
facts). They must not gain `GrainFactory`, streams-as-bus, or raw timers.

### Multi-owner

Stage 1: **one owner per deployment** (CONTEXT.md). Isolation = separate deployments, not
tenant keys in `NeuronId`. Multi-principal IdP is Kernel/edge — not Core address space.

---

## 6 · Package tree

Conceptual layout (responsibilities, not a promise of today's disk):

```text
src/
  DigitalBrain.Abstractions/          # ZERO deps — the only ABI
    Synapse.cs
    INeuron.cs
    NeuronId.cs
    SynapseMetadata.cs

  DigitalBrain.Core/                  # Orleans power lives here
    Neuron.cs                         # base grain, turn open/commit/poison, Emit/Reply/Ask
    NeuronOfState.cs                  # Neuron<TState> lazy slot
    Neuron.Dispatch.cs                # drain, FIFO, terminal DeliveryFailed
    Neuron.Asks.cs                    # open asks, Answers stamp, AskExpired
    Neuron.Connections.cs             # Connect/Disconnect, ghost rule, ConnectionRefused
    Neuron.Schedule.cs                # schedule table, ticks, ScheduleFailed
    Neuron.Transport.cs               # ITransport Deliver/Read — direct grain only
    NeuronConcurrency.cs              # serialized turns, interface whitelist
    NeuronJournal.cs                  # entries, tallies, compaction floor, committed watermark
    JournalEntry.cs                   # closed durable schema (Cause/Answers/To internal shape)
    BodyCodec.cs                      # STJ + kind/body; no module types in Orleans journal codec
    Catalog.cs                        # Build, fingerprint, listeners, answerers
    DeliveryPolicy.cs                 # bounds, horizons
    OutboxWakeup.cs                   # reminder backstop grain
    CoreSynapses.cs                   # Connect, DeliveryFailed, Schedule, … (Core assembly)
    IAnswers.cs                       # typed answerer (Core, not Abstractions)
    Brain.cs                          # Brain + Session edge
    JournalReading.cs                 # JournalFact, NeuronReading (public read models)
    Filters/
      SynapseHeaders.cs               # envelope ↔ RequestContext (no AQN)
      IncomingSynapseFilter.cs
      OutgoingSynapseFilter.cs
    Hosting/
      DigitalBrainSiloExtensions.cs   # AddDigitalBrain, DI gatekeeper, filters, journal format
    Placement/                        # optional: session-local / system fixed (only if measured)
    Streams/                          # edge adapters ONLY — ingress/egress helpers, not n2n bus

  DigitalBrain.Testing/               # cluster fixtures, clocks, commit faults, journal asserts
    DigitalBrainTest.cs
    NeuronTest.cs
    BrainTestClusters.cs
    RecordingJournalStorageProvider.cs
    …

  DigitalBrain.Kernel/                # LATER — not Core
    # behavior ALC gate, ModuleActivated facts, capability gate, owner tap

modules/                              # product vocabularies — neurons + synapses only
samples/
```

**Non-packages:** no `DigitalBrain.Bus`, no `DigitalBrain.Streaming` product surface for n2n,
no `DigitalBrain.Abstractions.Answers`.

---

## 7 · Scenario coverage matrix

| Scenario theme | Core capabilities used | Explicit non-use |
|---|---|---|
| **Enrichment pipelines** | Chain of `INeuron<T>` Emit stages; journals reconstruct causality; each stage own IO | No pipeline orchestrator grain; no stream-per-stage |
| **Social → dashboard** (north-star) | Ingress module Emits posts; `Connect` rewires account→behavior→chart; chart Emits UI fact; Flutter module listens | No string `[WireTo]`; no global timeline |
| **Recall / "why"** | `Brain.ReadAsync` journals + connection tables; tallies outlive compaction; causal walk via entry Cause/From/To | Metadata Cause API for modules; synthetic "why" without journals |
| **Rich multimodal chat** | Session context name isolation; long turns await model IO; tools as facts (`Ask`/`Emit`); deferred answers; optional interleave on **read** only | Sync capability await inside tool that re-enters same chat neuron; streams as transcript authority |
| **Live scripts** | Behavior compiles to neuron kinds; same catalog dispatch; Connect for instance wiring | Script VM beside grains; script calling `GrainFactory` |
| **Multi-owner** | Deployment isolation; session contexts per conversation | Tenant id in NeuronId; shared silo multi-tenant Core |
| **N+1 install** | New module assembly → catalog rebuild / epoch; zero code change in speakers (declarations) | Runtime registry repair on every activation; dual derivation |
| **Long-running progressive work** | Multi-turn fact protocols; `TState` join; Schedule pulses; open asks + AskExpired; outbox survives restart | Holding one turn for entire workflow; workflow engine package |
| **Cancel / replan** | New facts (`Cancel*`, `Replan*`) as vocabulary; schedule Unschedule; open asks expire; handlers ignore stale via state generation | Distributed transaction rollback; silent drop without journal |

### Stress notes (honest)

- **Enrichment + multimodal:** fan-out is free; **join** is always `TState` or journal read — not
  a Core join primitive. Correct.
- **N+1:** Stage 1 stop-the-world / blue-green fingerprint; hot Revision is Kernel Stage 3.
- **Cancel:** Core does not invent cancel tokens across neurons; cancellation is domain facts +
  delivery CancellationToken for **in-flight IO only**. A committed emission is not un-said;
  compensating facts are the model.

---

## 8 · Grill log (attacks defeated)

Each item: **Attack → Defense → Decision.**

### G1 · Four types cannot express ask/answer

**Attack:** Without `Synapse<TReply>` and dual `INeuron`, edge inference and answerer cardinality
die; FLOWS 1/3/5 collapse.  
**Defense:** Typed answering is a **Core protocol** (`IAnswers<,>`, `Ask`/`AskAsync`), not an ABI
fork of every fact. Listeners stay one interface.  
**Decision:** Keep Abstractions at 4. Put `IAnswers` in Core. Edge `AskAsync<TReply>` carries the
reply type parameter.

### G2 · Relocating CoreSynapses to Core forces modules to reference Core

**Attack:** "Abstractions-only modules" cannot listen to `DeliveryFailed`.  
**Defense:** A module that reacts to delivery physics is a Core consumer by definition. Pure
vocabulary packs that only declare facts need Abstractions; behavioral modules already need
`Neuron`.  
**Decision:** CoreSynapses ship in Core. Accept the reference. Do not bloat Abstractions for
symmetry cosplay.

### G3 · Cause on metadata is required for "why"

**Attack:** Without Cause on `SynapseMetadata`, reconstructability is lost.  
**Defense:** Cause lives on **journal entries**. Public metadata is identity. Introspection reads
journals, not ambient envelopes. Authors never stamp Cause.  
**Decision:** Thin metadata. Journal structure is causation.

### G4 · Streams must be the bus for scale

**Attack:** Direct calls won't scale; implicit streams + pub-sub are "real Orleans".  
**Defense:** Final/v4 proved memory streams lose late subscribers; dual bus + string keys caused
silent permanent deafness. Scale fan-out with outbox parallelism and placement, not a second
lossy truth.  
**Decision:** Streams = edge only. Direct Deliver + journal = causal bus.

### G5 · Implicit stream subscriptions are free declaration-is-subscription

**Attack:** Map each `INeuron<T>` to an implicit stream namespace; delete catalog.  
**Defense:** Stream subscription ≠ durable journaled delivery; activation order and caching break
"hearing IS the behavior"; topology becomes invisible to journal introspection.  
**Decision:** Catalog declarations remain the neuron subscription model. Implicit streams only
for ingress adapters that journal immediately.

### G6 · Connections recreate v1 Subscribe dual-audience

**Attack:** Declaration ∪ connection is Subscribe under a new name.  
**Defense:** v1 killed itself with *remote registry lookup in the emit path*, correlation-named
receivers, and dual derivation. Connections are **local durable state on the emitter**, mutated
by journaled facts, snapshotted at commit — no lookup, no timeout retract.  
**Decision:** Keep two-source union; ban registry grains and emit-path RPCs.

### G7 · In-turn Send is required for OS claim ("any behavior")

**Attack:** Without neuron `Send(address)`, behaviors cannot target learned addresses.  
**Defense:** Across ten flows + north-star, directed sends are edge `Session.SendAsync` (Connect)
or Reply-to-source. Instance targets are connection table rows.  
**Decision:** Stage 1: edge Send only. Add in-neuron Send when Kernel behavior-creator is a real
consumer — not before.

### G8 · Reply verb reintroduces Handling / correlation-as-API

**Attack:** Reply needs source → ambient envelope → same disease as `protected Handling`.  
**Defense:** Source is **Core-private** turn state; modules get `Reply(fact)` only — no Sequence,
no Cause, no envelope fields.  
**Decision:** Reply verb yes; Handling metadata no.

### G9 · Full Orleans surface will bloat Neuron into 600 lines

**Attack:** Reminders + streams + placement + versioning + transactions = Projects/digitalbrain
again.  
**Defense:** Partial classes by responsibility; streams in edge adapter files; transactions
non-goal; placement optional; modules cannot see Orleans. Line budget is enforced by file list.  
**Decision:** Orleans power is **in Core**, sealed **from modules**. Feature flags by consumer.

### G10 · Stateless workers for pipeline stages

**Attack:** Transcriber/Summarizer should be stateless workers for scale.  
**Defense:** Without stable identity there is no journal, no watermark, no "why", no at-least-once
dedup. Pipeline stages that matter are neurons.  
**Decision:** Default DurableGrain neurons. Stateless only for proven non-authoritative offload.

### G11 · Transactions for multi-neuron ACID

**Attack:** Fan-in briefing needs atomic multi-grain commit.  
**Defense:** Join state is one neuron's `TState`; legs are facts; restart resumes. Multi-grain
tx couples activation lifetimes and is almost never what agents want.  
**Decision:** Non-goal. Saga-by-facts is the model.

### G12 · Answer<> is the only zero-ceremony continuation

**Attack:** Without Answer, Planner must store questions in fields → ceremony tax.  
**Defense:** FLOWS that need join already use `TState` (Briefing). Many continuations only need
the reply body. Answer reconstruction is Core cost and shape-drift risk.  
**Decision:** Stage 1: bare replies + TState. Revisit Core-only dispatch view if ceremony proves
real in BDD counts — not in Abstractions.

### G13 · Global timeline for overhear / audit

**Attack:** One stream of all facts simplifies overhear.  
**Defense:** Documented silent loss; unbounded; dual truth with journals. Overhear = declare
`INeuron<T>` at context. Audit = read journals.  
**Decision:** No global timeline bus.

### G14 · Fat IDigitalBrain for "ergonomics"

**Attack:** One facade for send/ask/subscribe/watch/install/journal keeps UX simple.  
**Defense:** Mega-interface becomes the god object and freezes wrong seams. Edge is `Brain` +
`Session` with three speech verbs and one read.  
**Decision:** Thin edge. No IDigitalBrain kitchen sink in Core.

### G15 · System.Type in NeuronId for safety

**Attack:** String kinds collide and lose compile-time checks.  
**Defense:** Kind collisions fail boot; journals and grown modules cannot store Type/AQN;
upgrades would mint new brains.  
**Decision:** `NeuronId(Kind, Name)` strings only.

### G16 · Same-turn reply ride-back for edge latency

**Attack:** Edge needs sync ask for HTTP timeouts.  
**Defense:** Ride-back is a second delivery path that bypasses outbox FIFO → deterministic loss
under retry (grill FATAL in design history). Edge observes journal; optional later optimization
must still journal first.  
**Decision:** No same-turn reply as correctness path. Commit then deliver.

### G17 · In-turn registry "just with cache" 

**Attack:** Cache connection directory; only miss pays timeout.  
**Defense:** Miss path still retracts/blocks emits; cache coherence is Subscribe repair again.  
**Decision:** Local emitter tables only.

### G18 · Behaviors as scripts that call neurons like RPC

**Attack:** Scripting ergonomics want `await brain.CallAsync<Greeter>(...)`.  
**Defense:** Reintroduces neuron-awaits-neuron and single-thread occupation deadlocks.  
**Decision:** Scripts *are* neurons speaking facts. No RPC veneer in Core.

### G19 · Dual packages for "Core without Orleans" testing

**Attack:** Abstract bus behind interfaces for unit tests without silo.  
**Defense:** Dual derivation / dual truth. Testing package runs real clusters; doubles only at
module IO ports.  
**Decision:** One Core implementation. Tests use `DigitalBrain.Testing` clusters.

### G20 · Store correlation IDs for OTel

**Attack:** Need correlation on metadata for traces.  
**Defense:** Activity/traceparent is telemetry projection; causal ids are `SynapseRef` structure
in journals.  
**Decision:** No correlation fields on Abstractions metadata.

---

## 9 · Open risks (honest, max 5)

1. **Continuation ceremony without `Answer<,>`.** If multi-turn modules accumulate painful
   TState boilerplate, pressure will rise to re-add a Core dispatch view. Hold the line on
   Abstractions; measure ceremony in real BDD before growing Core.
2. **Ask routing by context name.** Wrong-context asks mint empty parallel worlds (virtual
   actors). Mitigated by docs + tests + edge session discipline; not by Core magic.
3. **Catalog stop-the-world on N+1 install (Stage 1).** Hot Revision is Kernel work; until then
   module add is deploy-shaped. Do not fake hot-load.
4. **Stream misuse under delivery pressure.** Teams will want streams for "speed". Only
   gatekeeping + code review + this document prevent a second bus; consider analyzer later.
5. **Single open-ask per question kind per activation.** Backpressure via refusal is correct but
   sharp for chat tool storms; may need explicit multi-ask keys later — only with a consumer.

---

## 10 · What to build next (ordered, small)

1. **Reset Abstractions to exactly the four types** — delete `Answer`, `CoreSynapses`,
   `JournalFact`, `SynapseRef`, `Synapse<TReply>`, dual `INeuron` from the Abstractions project;
   fix compile breaks by relocating types into Core.
2. **Core `IAnswers<,>` + thin `SynapseMetadata`** — journal entries keep Cause/Answers; public
   metadata does not.
3. **Turn pipeline vertical slice** — Deliver → handle → stage → one commit → poison on fault
   (no drain yet); tests: thrown handler leaves zero durable trace.
4. **Journal-as-outbox drain** — watermark dedup, FIFO blocked targets, `DeliveryFailed`, wakeup
   reminder/timer; tests: restart survival, redelivery.
5. **Emit resolution** — catalog declarations + same-context fan-out; zero-receiver legal.
6. **Connections** — Connect/Disconnect, ghost rule, ConnectionRefused; north-star wiring test.
7. **Ask/Reply/edge AskAsync** — open asks, deferred null, AskExpired; greeter round trip.
8. **Schedule** — verbs + facts, ScheduleFailed; pulse flow test.
9. **Brain/Session edge + ReadAsync** — journal read models in Core; causal reconstruct test.
10. **Edge stream adapter (optional)** — one ingress or UI push proof; assert it is **not** on the
    n2n path.
11. **Catalog fingerprint multi-silo** — mismatch refuses join.
12. **Stop.** Do not start Kernel behavior ALC, transactions, or IDigitalBrain facade.

Each step ends green under `DigitalBrain.Core.Tests` with public API only. No red root gate. No
feature without a consumer test in the same change.

---

## Non-goals / forbidden patterns (checklist)

- Fat Abstractions (>4 types) or "just one more" envelope field for convenience  
- `IDigitalBrain` mega-interface  
- Streams as neuron-to-neuron causal bus; global timeline as sole bus  
- In-turn remote registry / directory lookup on emit  
- `System.Type` / AQN / GUID synapse ids in durable addresses or journal schema  
- Neuron awaiting neuron; same-turn reply as source of truth  
- Module-visible `WriteStateAsync`, `GrainFactory`, raw timers, `IRemindable`, extra grain ifaces  
- Dual derivation of topology (source-gen table + reflection)  
- Correlation/causation fields on public metadata for author use  
- Stateless workers as default neuron host  
- Orleans Transactions as default coordination  
- Behaviors as a second VM or RPC layer  
- Faked proofs, synthetic journal observations, "durable" names on volatile stores  
- Drain awaiting Deliver inside the emitting handler turn (reentrancy deadlock class)

---

---

## 11 · Second grill (50 scenarios + full Orleans surface)

Adversarial pass after writing `scenarios/01`–`50`. Question: does thin Core + full Orleans still
hold when every scenario class exists?

### G21 · "Use all Orleans features" means streams must be the n2n bus

**Attack:** Scenario 10/28/47 demand pub-sub and implicit wake; without stream bus Core is incomplete.  
**Defense:** Those scenarios need *ambient fan-out* and *ingress wake*, not a second causal log.
Emit→catalog∪connections is pub-sub. Implicit streams wake **ingress adapters** that journal
once (sc28). UI dashboards bind via journal watchers or edge stream **mirrors** of committed
facts (sc10, sc47).  
**Decision:** Stand. Orleans streams are **in Core** as edge adapters; not the authority path.

### G22 · Stateless workers for 10k embeddings (sc45)

**Attack:** Scenario 45 requires stateless workers; forbidding them starves Orleans power.  
**Defense:** Embeddings are pure compute with no journal identity. Core may host a **non-Neuron**
stateless-worker grain type for offload only — modules call it via service, not as `INeuron`.
Never a journaled actor.  
**Decision:** Allow stateless workers **beside** neurons, never **as** neurons.

### G23 · Multi-owner shared silo (sc25, sc27)

**Attack:** Scenarios assume multi-owner; architecture says one owner per deployment.  
**Defense:** Product may later add owner keys; Stage-1 Core stays single-owner-per-deployment to
avoid dual identity schemes. Isolation scenarios pass as separate catalogs / deployments.  
**Decision:** Stage 1 deployment isolation. Revisit only with Kernel identity design.

### G24 · Hot-reload catalog (sc24, sc26, sc49)

**Attack:** N+1 install and behavior activate need live catalog mutation.  
**Defense:** Stage 1 = deploy/fingerprint; Kernel Stage 3 = Revision epoch. Faking hot-load without
epochs recreates dual derivation.  
**Decision:** Scenarios 24/26/49 are **Kernel+Core epoch** work, not Stage-1 Core alone. Core
exposes fingerprint/epoch hook only.

### G25 · Nested asks (sc37) reintroduce neuron-awaits-neuron

**Attack:** Chat asks memory asks vector = nested awaits.  
**Defense:** Each ask is fact protocol: open ask, later reply turn, join in `TState`. Never
`await otherNeuron.Handle`.  
**Decision:** Stand. Nested *workflows* yes; nested *grain awaits* no.

### Orleans feature → scenario proof (must be green eventually)

| Orleans feature | Scenario IDs that prove it |
|---|---|
| DurableGrain + journaling | 01–04, 09, 34, 48 |
| Serialized turns + anti-reentrancy | 07, 30, 37 |
| Call filters + RequestContext | 05, 25, 43 |
| Timers + reminders | 02 (sample windows), 39, 46 |
| Direct Deliver + outbox | all pipelines; 35 DeliveryFailed |
| Catalog broadcast (Emit) | 02, 10, 47, 50 |
| Connections / rewiring | 02, 05, 36 |
| Streams explicit (edge) | 10, 47 |
| Streams implicit (ingress wake) | 28 |
| Placement | 13, 19 |
| Stateless worker (non-neuron) | 45 |
| Grain versioning / epoch | 24, 26, 44, 49 |
| Journal read / watchers | 03, 04, 34, 48 |
| Behavior-as-neuron | 05, 19, 36, 49 |
| Multimodal UI facts | 06, 33, 38 |
| Progressive multi-turn | 29, 30 |
| Schedule / batch | 39, 46, 50 |

### Abstractions alignment with branch `v2-abstractions`

Keep **exactly four types**. Field name on address:

| Branch `v2-abstractions` | Ratified |
|---|---|
| `NeuronId(string Type, string Name)` | `NeuronId(string Kind, string Name)` |

`Type` reads as CLR type; durable kind is a **string convention**, not `System.Type`. Rename is
ABI-compatible in spirit; do not reintroduce `System.Type` storage.

---

## 12 · Scenario folder

Fifty scenario markdown files live in [`scenarios/`](scenarios/README.md). They are the
acceptance language for Core: if a green suite cannot support a scenario’s choreography, either
the suite is incomplete or the architecture lied.

---

*Ratified for implementation after two grill passes + 50 scenario stress. Prefer delete.
If two mechanisms do the same job, keep one.*
