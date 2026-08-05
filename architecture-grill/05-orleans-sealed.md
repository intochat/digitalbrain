# 05 — Orleans power IN Core, SEALED from modules

Date: 2026-08-05. Status: **RATIFIED**.  
Scope: every Orleans feature Core may use; what modules never see; how `NeuronConcurrency` and grain call filters enforce the seal.  
Inputs: live `DigitalBrain.Core` (`Neuron*`, `NeuronConcurrency`, filters, `OutboxWakeup`, hosting gate), `CORE-ARCHITECTURE.md` §3/G9–G11/G21–G25, product trap (reentrancy deadlock), scenarios 10/28/44/45/46/47.  
Method: brainstorm → self-grill → delete-first → one mechanism per job.

---

## 0 · Thesis (one sentence)

**Orleans is Core’s body; the module ABI is neurons, synapses, and turn verbs.**  
Every Orleans type, attribute, grain interface, stream, reminder, placement strategy, and transaction API is either (a) Core-internal machinery, (b) a Core-owned non-neuron grain beside the brain, or (c) forbidden. Modules never import Orleans.

---

## 1 · Seal layers (defense in depth)

| Layer | When | What it blocks |
|---|---|---|
| **Package / project** | Compile | Module assemblies reference `DigitalBrain.Abstractions` + `DigitalBrain.Core` only — never `Microsoft.Orleans.*` packages. Prefer no Orleans global usings in module projects (v1 samples already strip them). |
| **Escape-hatch sealing** | Compile | `Neuron` re-hides `WriteStateAsync`, `GrainFactory`, `DeactivateOnIdle` as `[Obsolete(..., error: true)]` throwing — modules cannot call them even when they inherit the grain. |
| **Activation guard** | First activation | `NeuronConcurrency.RequireSerializedTurns(GetType())` — attributes, extra `IAddressable` contracts, `IRemindable`, `StatelessWorker` on neuron types. |
| **Durable-key gate** | Activation / first durable register | `GatedStateManager` admits only Core journal keys; module-minted `IDurable*` keys fail loudly. |
| **Incoming call filter** | Every wire call into a neuron | Whitelist: Core transport / drain / session + Orleans runtime interfaces. Anything else = second wire. |
| **Outgoing call filter** | Every wire call from a neuron | Self-proxy → loud fail (proven deadlock). Delivery envelope → `RequestContext` headers. |
| **Wire type filter** | Serialization | Only Core grain interfaces are vouched; module speech is `Synapse` JSON, not custom grain RPC types. |
| **Catalog boot** | Silo compose | Kind collisions, reserved-kind hijack, TState codec contract, dead answerer claims — before the silo forms. |

If a module “needs Orleans” for a feature, the feature is either missing from Core’s *vocabulary* (Schedule, Connect, Ask, journal read) or is Kernel/edge work — not a license to open the grain.

---

## 2 · RATIFIED Orleans feature matrix

Legend:

| Visibility | Meaning |
|---|---|
| **Core-internal** | Used inside `DigitalBrain.Core` (and Core-owned non-neuron grains). Modules never name the type. |
| **Module-visible (vocabulary)** | Modules get a *Core verb / fact / DI service* that *hides* the Orleans mechanism. |
| **Forbidden** | Not used; activation/filter/package must refuse if attempted. |
| **Optional later** | Not Stage-1; may land when a real consumer + scenario proof exists. Never module-visible Orleans API. |

### 2.1 Master table (RATIFIED)

| Orleans feature | Core use? | Module-visible? | Status | Module surface (if any) | Enforcement | Scenario proof |
|---|---|---|---|---|---|---|
| **Serialized turns (default grain single-thread)** | **Yes** — load-bearing physics | Implicit (turn model) | **Core-internal + contract** | One turn at a time; no await-other-neuron | Default activation; tests | 07, 30, 37 |
| **`[Reentrant]` on neuron** | No | No | **Forbidden** on neurons | — | `NeuronConcurrency` refuse | — |
| **`[Reentrant]` on Core helper grains** | Yes (`OutboxWakeup`) | No | **Core-internal only** | — | Not a `Neuron` subclass | 46, drain |
| **`[MayInterleave]`** | No on neurons | No | **Forbidden** on neurons | — | `NeuronConcurrency` refuse | — |
| **`[AlwaysInterleave]`** | Yes — **only** Core journal/health read methods | No (modules cannot declare) | **Core-internal surgical** | `Brain.ReadAsync` / transport read may proceed during long turns | Core puts attr on `ITransport` reads; activation refuses any module method/iface with it | 03, 04, 34, long chat |
| **`[ReadOnly]`** | Yes — same surface as read interleave | No | **Core-internal surgical** | Committed-truth reads only | Same as AlwaysInterleave; paired on read methods | 03, 04 |
| **Incoming grain call filters** | Yes | No | **Core-internal** | — | Registered only by `AddDigitalBrain` | 05, 25, 43 |
| **Outgoing grain call filters** | Yes | No | **Core-internal** | — | Same | self-call, envelope |
| **`RequestContext`** | Yes — envelope transport only | No | **Core-internal** | Never author API; never authority | `SynapseHeaders` only; journal is truth | all Deliver |
| **`DurableGrain` + Orleans.Journaling** | Yes — neuron base | No (raw API sealed) | **Core-internal** | `TState`, Emit/Ask/Schedule, journals via `ReadAsync` | Escape hatch obsolete; durable-key gate | 01–04, 34, 48 |
| **Module-minted `IDurable*` / extra journal keys** | No | No | **Forbidden** | All durable module state = `TState` | `GatedStateManager` | — |
| **Module `WriteStateAsync`** | No | No | **Forbidden** | Core one-batch commit only | Obsolete error + throw | — |
| **Grain timers (`RegisterGrainTimer`)** | Yes — drain pulse + schedule ticks | No raw API | **Core-internal**; **module-visible as Schedule** | `Schedule` / `Unschedule` verbs + `Schedule`/`Unschedule` facts | Modules never call timer APIs; only Core arms | 02, 39, 46, 50 |
| **Reminders (`IRemindable`, register/unregister)** | Yes — `OutboxWakeup` backstop; schedule idle wake | No | **Core-internal**; **module-visible as Schedule** | Same Schedule vocabulary | `IRemindable` on neuron refused; wakeup is separate grain | 46, restart |
| **Direct grain calls (`Deliver` / transport)** | Yes — sole n2n bus after commit | No (`GrainFactory` sealed) | **Core-internal** | Emit / Reply / Ask / edge Send | Outgoing self-proxy filter; interface whitelist | all pipelines; 35 |
| **Streams — explicit** | Edge adapters only | No Orleans stream types | **Core-internal edge**; optional Stage-2 | UI/SSE projection, telemetry mirror, high-volume ingress→journal | Package + review + later analyzer; never n2n authority | 10, 47 |
| **Streams — implicit subscriptions** | Ingress adapter auto-activation only | No | **Core-internal edge** (optional) | External bus → adapter grain → first journaled Deliver | Same; catalog remains neuron subscription model | 28 |
| **Placement strategies** | Optional measure | No attributes on modules | **Optional later (Core)** | Prefer-local session; fixed system kinds | Core placement directors only; never load-balance identity away | 13, 19 |
| **`[StatelessWorker]` on neuron** | No | No | **Forbidden** | — | `NeuronConcurrency` refuse | — |
| **Stateless worker grains (non-Neuron)** | Yes when proven pure compute | Via **DI / Core offload seam**, not as `INeuron` | **Optional later (Core beside neurons)** | Module calls a registered service; Core may implement with SW grains | SW never subclass `Neuron`; no journal/watermark | 45 |
| **Grain versioning / rolling upgrade** | Catalog fingerprint + epoch (Kernel Stage 3) | Fingerprint visibility only | **Core seam; Kernel owns product epoch** | Redeploy / Revision — not per-message version in ABI | Fingerprint mismatch refuses join (when cluster gate lands) | 24, 26, 44, 49 |
| **Orleans Transactions** | No | No | **Forbidden (non-goal)** | Fact sagas + `TState` join | Do not register tx; no module enlistment API | — |
| **Grain services / DI in grain ctor** | Yes — primary ctor + silo DI | Yes as **normal .NET DI** | **Core hosts; modules consume services** | `HttpClient`, model clients, stores — never `IGrainFactory`/`IDurable*` | Host registers services; Core does not expose second container | all IO modules |
| **Grain extensions / cancellation (Orleans runtime ifaces)** | Yes — pass through filter | No | **Core-internal / runtime** | — | Incoming filter allows `Orleans*` namespaces | — |
| **Observers / client streams as n2n** | No | No | **Forbidden** as causal bus | Edge push only | Same as streams policy | — |
| **`IGrainWith*` extra keys on neurons** | String key only (`kind` type + name key) | `NeuronId` only | **Core-internal addressing** | `NeuronId(Kind, Name)` | Extra grain ifaces refused | identity tests |
| **Multi-cluster / geo** | Out of Stage-1 | No | **Optional later / product** | — | Not Core Stage-1 | — |

**Ratification line:** the matrix is closed for Stage 1. Adding a row to “module-visible Orleans type” requires a new grill pass and a named consumer that cannot be expressed as a fact or DI service.

---

## 3 · Per-feature deep dive

### 3.1 Reentrancy / interleave / readonly

**Physics we protect**

1. Journal order and watermark progression assume one mutating turn at a time per activation.
2. Drain awaits remote `Deliver` **outside** the emitting handler turn (post-commit timer/reminder turns). If a handler re-enters the same neuron while Drain holds the turn, **deadlock** (v1 trap: `DrainAsync` → `Deliver` → re-enter emitter).
3. Self-delivery must be a **direct method call**, never the grain proxy (same deadlock class).

**RATIFIED rules**

| Mechanism | Neurons | Core non-neuron grains |
|---|---|---|
| Default serialized | Required | Default unless proven |
| `[Reentrant]` | **Forbidden** | Allowed when races are harmless (`OutboxWakeup` Arm/Disarm vs tick) |
| `[MayInterleave]` | **Forbidden** | Avoid unless measured |
| `[AlwaysInterleave]` | **Forbidden on module-visible surface**; **allowed only** on Core-owned **committed read** methods | N/A |
| `[ReadOnly]` | Same as AlwaysInterleave — **read surface only** | N/A |

**Why AlwaysInterleave + ReadOnly together on reads**

- `ReadOnly` alone does not let a journal query cut in front of a long non-readonly turn (model call, HTTP).
- `AlwaysInterleave` alone without ReadOnly risks concurrent mutation if mis-applied.
- Pairing both on `ITransport.ReadAsync` / `ReadStateAsync` (and only those) is the surgical exception: **committed truth** is snapshot-stable under Orleans.Journaling after `MarkCommitted`; reads must not open turns or stage emissions.

**Modules never see:** any concurrency attribute, any “make this handler reentrant” escape.

---

### 3.2 Call filters + RequestContext

**Core jobs only**

| Filter | Job | Non-job |
|---|---|---|
| `IncomingSynapseFilter` | Admit Core surface; bind delivery envelope; pass non-neuron grains | Business auth, capability tokens (Kernel later) |
| `OutgoingSynapseFilter` | Block self-proxy; write envelope headers on delivery methods | Dual bus, “smart” routing |

**RequestContext policy**

- **Transport convenience only** — kind/name/seq/timestamp/cause/answers refs as header keys (`SynapseHeaders`).
- **Not authority** — redelivery rehydrates body from **journal bytes**; missing envelope on Deliver = kernel bug, not soft degrade.
- **Not module API** — modules never `RequestContext.Set`; no “correlation id for authors” on the ambient envelope (telemetry may use Activity separately).

**Filter whitelist (incoming) — RATIFIED**

A call into `context.Grain is Neuron` is admitted iff `InterfaceMethod.DeclaringType` is one of:

1. `Neuron.ITransport` — Deliver, DeliverQuestion, Read, ReadState  
2. `Neuron.IDrainEntry` — Drain  
3. `Neuron.ISessionEntry` — edge session speech/entry  
4. Any type whose namespace starts with `Orleans` — runtime extensions (cancellation, etc.)

Everything else → `InvalidOperationException` naming the whitelist rule.

Non-`Neuron` grains (`OutboxWakeup`, future edge stream adapters, optional stateless workers) pass through untouched by the neuron whitelist (they have their own contracts).

**Outgoing rules — RATIFIED**

1. `SourceContext.GrainId == TargetId` → throw (self-proxy deadlock).  
2. If source is `Neuron` and method is delivery → `SynapseHeaders.Write(sender.TakeOutboundDelivery())`.  
3. Invoke.

---

### 3.3 DurableGrain / journaling

**Core owns the entire durable mutation surface of a turn.**

| Structure | Owner | Module sees |
|---|---|---|
| Journal entries (heard/said) | Core | Via `ReadAsync` / `JournalFact` read models |
| Watermarks, progress, blocked targets | Core | `DeliveryFailed` facts when terminal |
| Connections table | Core | `Connect`/`Disconnect` facts |
| Schedule table | Core | `Schedule`/`Unschedule` verbs + facts |
| Open-ask pins | Core | Ask/Reply protocol |
| `TState` slot | Core storage, module shape | `protected TState State` on `Neuron<TState>` |

**One batch commit** after handler returns; commit failure → poison activation → reload committed truth. No module `WriteStateAsync`. No second journal. Body codec = JSON kinds; Orleans.Journaling never sees module CLR types as grain state schema.

---

### 3.4 Timers / reminders — modules may “own time” (reconciled)

**User claim:** modules may own time.  
**Risk:** modules register raw timers/reminders → second lifecycle, unenlisted durable work, silent timer-swallowed failures, bypass of journaled schedule table.

**Reconciliation (RATIFIED)**

| Who | Owns | Mechanism |
|---|---|---|
| **Module** | *What* fires and *when* in domain terms | `Schedule(fact, period)` / `Unschedule<T>()` in-turn; or emit/receive `Schedule`/`Unschedule` facts; handler must declare `INeuron<TFact>` for the tick body |
| **Core** | *How* time is kept across activation, idle, restart | Grain timers mirror committed schedule table; `OutboxWakeup` reminders backstop idle neurons; ticks enter **ordinary turn pipeline** via self-delivery; failures → `ScheduleFailed` after limit |
| **Module** | Wall-clock interpretation in handlers | Injected keyed `TimeProvider` (`NeuronTime.ServiceKey`) for domain logic — **not** Orleans’ unkeyed clock (activation collector must not see test epochs) |

**Modules never:**

- `RegisterGrainTimer` / `RegisterOrUpdateReminder` / implement `IRemindable`
- Hold “private” pulse without a scheduled fact (invisible to journal / “why”)
- Use high-frequency reminders for chat tokens (timer + fact for in-activation; reminder is backstop)

**Idle 30-day wake (sc46):** schedule entry + reminder backstop → activation → tick → journaled fact. Continuity is a **durable scheduled fact**, not a calendar row outside the brain.

---

### 3.5 Streams (explicit / implicit)

| Use | Allowed? | How |
|---|---|---|
| UI / SSE / many dashboards | Yes (edge) | Projector neuron or edge adapter mirrors **committed** facts |
| Telemetry | Yes (edge) | Mirror only |
| Ingress (webhooks, X firehose) | Yes | Stream → **adapter grain** → first `Deliver` + journal |
| Implicit stream = `INeuron<T>` subscription | **No** | Catalog owns declaration-is-subscription |
| Stream as sole n2n delivery | **No** | Documented late-join loss; dual truth |
| Global timeline as bus | **No** | Unbounded + dual truth |

Late-join causal history = **journal read**, never stream replay. Stage-1 may ship **zero** stream code if no edge consumer is ready; policy still stands so teams do not invent a second bus under load.

---

### 3.6 Placement

| Strategy | Stage-1 | Notes |
|---|---|---|
| Default Orleans placement | Yes | Identity is grain id, not silo |
| Prefer-local / session sticky | Optional later | Measure chat/session latency (sc13, sc19) |
| Fixed placement for system grains | Optional later | Wakeup / well-known infrastructure |
| Random load-balance of stateful neurons | **Forbidden** | Breaks locality assumptions; does not break identity but destroys cache heat and confuses operators |
| Module-declared `[Placement*]` attributes | **Forbidden** | Placement is Core/host policy |

---

### 3.7 Stateless workers

| Form | Decision |
|---|---|
| `[StatelessWorker]` on a `Neuron` subclass | **Forbidden** — no stable journal identity, no watermark, no “why” |
| Separate non-Neuron SW grain for pure compute (embed, encode) | **Optional later** — orchestrator neuron journals progress; workers are **stateless offload** behind a DI service or Core-owned grain iface modules never implement |
| Module “becomes” SW for scale | **No** — scale with more named neurons + outbox parallelism + optional offload |

Scenario 45 is the proof shape: `NotesIndexer` neuron owns truth; embed pool has no owner journal.

---

### 3.8 Grain versioning

| Layer | Owner | Module sees |
|---|---|---|
| Catalog fingerprint at boot | Core | Log / refuse join on mismatch |
| Hot Revision / behavior epoch | Kernel Stage 3 | Lifecycle facts, not Orleans version APIs |
| Orleans grain interface versioning attributes | Core only if wire evolves | Never in module contracts |
| Per-message version field in Abstractions | **Forbidden** | Kinds are string conventions; breaking fact shape = new kind or careful codec |

Rolling module deploy (sc44) is **process/catalog epoch**, not modules calling versioning APIs.

---

### 3.9 Transactions

**Non-goal.** Multi-grain ACID couples activation lifetimes and is almost never agent-correct (you want saga + journal, not two-phase abort of a model call).

Coordination model: facts + open asks + `TState` join + `DeliveryFailed` / `AskExpired`. Do not register Orleans Transactions in `AddDigitalBrain`. If a future scenario proves multi-grain ACID with no fact-saga, reopen grill — default remains forbidden.

---

### 3.10 Grain services / DI

| Injection | Allowed for modules? |
|---|---|
| `HttpClient`, `IChatClient`, stores, options | **Yes** — primary constructor / DI |
| `TimeProvider` keyed `NeuronTime.ServiceKey` | **Yes** for domain time |
| `IGrainFactory`, `IGrainContext`, `IDurable*`, `IPersistentState<>` | **No** |
| Second DI container inside module | **No** — host is the container |
| Resolving other neurons as grains | **No** — speak facts |

Core grains resolve `NeuronJournal`, `Catalog`, `BodyCodec`, keyed clock via `ServiceProvider` in the base — that is Core body, not a module pattern to copy for wire access.

---

## 4 · NeuronConcurrency — enforcement design (RATIFIED)

### 4.1 When it runs

`Neuron.OnActivateAsync` (sealed): first line of business after construction — `NeuronConcurrency.RequireSerializedTurns(GetType())` before `base.OnActivateAsync` / journal mark / resume dispatch. Fail activation = fail loud, no silent dual-mode neuron.

### 4.2 Checks (ordered)

```
RequireSerializedTurns(neuronType):
  1. Refuse [Reentrant] on type (inherit)
  2. Refuse [MayInterleave] on type (inherit)
  3. Refuse [StatelessWorker] on type (inherit)
  4. For methods on type + methods on interfaces NOT in CoreOwnedInterfaces:
       Refuse [AlwaysInterleave]
       Refuse [ReadOnly]
  5. Refuse IRemindable assignable
  6. Refuse any IAddressable-derived interface not in CoreOwnedInterfaces
       (second grain wire)
```

### 4.3 CoreOwnedInterfaces (whitelist of grain contracts a neuron may implement)

| Interface | Role |
|---|---|
| `Neuron.ITransport` | Deliver / DeliverQuestion / Read / ReadState |
| `Neuron.IDrainEntry` | Reminder/timer drain entry |
| `Neuron.ISessionEntry` | Edge session |
| `IGrainWithStringKey` | Key shape |
| `IGrain` | Orleans base |

**Not** on the list: any module-authored `ISomething : IGrainWith*`, any capability RPC interface, any “just one more” method for sync call.

### 4.4 Surgical interleave — who may wear the attributes

| Surface | AlwaysInterleave | ReadOnly | Notes |
|---|---|---|---|
| `ITransport.Deliver*` | **No** | **No** | Mutating turns |
| `ITransport.ReadAsync` / `ReadStateAsync` | **Yes** | **Yes** | Committed snapshot only; no turn open |
| `IDrainEntry.DrainAsync` | **No** | **No** | Mutates progress / may commit |
| `ISessionEntry.*` | **No** | **No** unless pure committed read is split later |
| Module methods / module grain ifaces | **No** | **No** | Activation refuse |

**Implementation note (v2 code today):** `ITransport` already uses `[ReadOnly]` on reads; pair `[AlwaysInterleave]` when long-turn journal observation is proven blocked (v1 contract). Activation scan **skips CoreOwnedInterfaces**, so Core attributes do not self-refuse; modules redeclaring reads do not inherit the licence (redeclaration on a non-core iface is refused).

### 4.5 What NeuronConcurrency deliberately does *not* do

- Does not parse handler IL for `GrainFactory` (sealed property throws; filter catches self-proxy).
- Does not enforce “no await other neuron” by static analysis Stage-1 (physics + culture + tests; analyzer later if needed).
- Does not replace the incoming filter (activation is type shape; filter is wire).

### 4.6 Test contracts (must exist / stay green)

- Interleave/ReadOnly only on Core read surface.  
- Any other AlwaysInterleave / ReadOnly → refuse.  
- Reentrant neuron type → refuse.  
- Extra grain interface → refuse.  
- IRemindable neuron → refuse.  
- StatelessWorker neuron → refuse.  
- Happy path greeter activates.

---

## 5 · Filter whitelist — full design (RATIFIED)

### 5.1 Incoming (neuron)

```
if grain is not Neuron → pass
if method.DeclaringType in { ITransport, IDrainEntry, ISessionEntry } → admit
if method.DeclaringType.Namespace starts with "Orleans" → admit  // runtime
else → throw second-wire
if IsDelivery(method) → envelope = SynapseHeaders.Consume() or throw kernel bug
                         receiver.AcceptEnvelope(envelope)
invoke
```

### 5.2 Outgoing

```
if source.GrainId == target → throw self-proxy deadlock
if source is Neuron && IsDelivery(method) → headers from TakeOutboundDelivery()
invoke
```

### 5.3 Wire type filter

Allow-list: `ITransport`, `IDrainEntry`, `ISessionEntry`, `IOutboxWakeup`.  
Module speech = `Synapse` (+ public read shapes) via JSON serializer voucher — not new grain RPC types.

### 5.4 Extending the whitelist

Adding a Core grain interface requires:

1. Documented job that cannot be a fact or DI service.  
2. Entry in `CoreOwnedInterfaces` **and** incoming whitelist **and** `CoreWireTypeFilter`.  
3. Grill note in this file.  
4. Tests for admit + refuse neighbors.

**Never** open the whitelist for “module convenience RPC.”

---

## 6 · What modules never see (checklist)

Modules **never** name, inherit, implement, or call:

- `Grain`, `DurableGrain`, `IGrainFactory`, `IGrainContext`, `GrainId` (except via Core-hidden base)
- `WriteStateAsync`, raw `IDurable*`, `IPersistentState<>`, journal storage providers
- `[Reentrant]`, `[MayInterleave]`, `[AlwaysInterleave]`, `[ReadOnly]`, `[StatelessWorker]`, placement attributes
- `IRemindable`, reminder registration APIs, raw grain timers
- Orleans Streams types / providers as application bus
- Orleans Transactions
- Custom `IGrainWith*` contracts on neurons
- `RequestContext` for product correlation
- Call filters registration
- `OutboxWakeup` / drain entry (Core-only)
- Deactivate-on-idle / activation lifetime knobs

Modules **do** see:

- `Synapse`, `INeuron<T>`, `NeuronId`, public metadata identity (Abstractions)
- `Neuron` / `Neuron<TState>` verbs: Emit, Ask, Reply (as designed), Schedule, Unschedule
- Core synapses they choose to handle (`DeliveryFailed`, `ScheduleFailed`, Connect, …)
- DI services for IO
- Edge: `Brain` / `Session` speech + journal read

---

## 7 · Grill log (this pass)

### G-O1 · “Modules own time” means raw reminders

**Attack:** Scenario 46 needs 30-day wake; only `IRemindable` is durable across deactivation — give modules reminders.  
**Defense:** Durable *intent* is the schedule table + journaled `Schedule` fact; Core’s wakeup grain holds the Orleans reminder. Modules own domain due times via Schedule vocabulary.  
**Decision:** **Module time = Schedule facts/verbs; Core time = timers+reminders.** Stand.

### G-O2 · AlwaysInterleave on tool handlers for “throughput”

**Attack:** Multi-tool chat wants parallel handlers on one neuron.  
**Defense:** One journal, one watermark, one outbox cursor — parallel mutating handlers corrupt turn physics and reintroduce Drain deadlock classes. Parallelism = multiple neurons or post-commit fan-out.  
**Decision:** Forbidden on handlers. Only committed reads interleave.

### G-O3 · Streams for dashboard scale (sc10/47)

**Attack:** 1k dashboards cannot take direct Deliver.  
**Defense:** Dashboards are edge projections; fan-out from a projector or stream **mirror of committed facts**. Causal bus stays journal+Deliver among brain neurons.  
**Decision:** Explicit streams edge-only. Stand.

### G-O4 · Implicit streams delete the catalog

**Attack:** Map each fact type to a stream namespace; drop catalog reflection.  
**Defense:** Subscription ≠ durable delivery; late join and activation order break “hearing IS the behavior”; topology invisible to journals.  
**Decision:** Catalog stays. Implicit = ingress wake only.

### G-O5 · Stateless worker neurons for pipelines

**Attack:** Every pure stage should be SW.  
**Defense:** No identity → no journal → no why → no at-least-once watermark.  
**Decision:** SW only as non-Neuron offload; default DurableGrain neurons.

### G-O6 · Transactions for multi-module ACID

**Attack:** Travel booking (sc15) needs atomic multi-grain.  
**Defense:** Booking is a fact saga with compensations and open asks; tx abort does not unsend email.  
**Decision:** Transactions forbidden.

### G-O7 · Expose GrainFactory “just for advanced modules”

**Attack:** Escape hatch behind `#if` or `protected` for power users.  
**Defense:** One advanced module reintroduces dual bus and self-proxy deadlock; seal is binary.  
**Decision:** Obsolete error + throw. No advanced hatch.

### G-O8 · Filter whitelist includes module interfaces “if they only read”

**Attack:** Allow `IChatView : IGrain` with ReadOnly for UI.  
**Defense:** Second wire into the grain; UI reads journals via Core `ReadAsync` / edge projection.  
**Decision:** Whitelist closed. Views are not grain contracts on neurons.

### G-O9 · RequestContext as source of truth for Cause

**Attack:** Persist less if envelope always carries Cause.  
**Defense:** Envelope dies with the call; redelivery and crash recovery need journal.  
**Decision:** RequestContext transport-only.

### G-O10 · Placement attributes on hot chat neurons

**Attack:** Module authors know affinity best.  
**Defense:** Placement is cluster policy; modules encode identity (`NeuronId`), not topology. Wrong placement attrs become undeployable folklore.  
**Decision:** Core/host only; optional later.

### G-O11 · Version field on every synapse

**Attack:** Rolling upgrade needs per-message version.  
**Defense:** Catalog fingerprint + kind evolution; AQN/version soup was v1 pain.  
**Decision:** No version in Abstractions; epoch at Kernel.

### G-O12 · DI inject IGrainFactory into module services

**Attack:** Helper service delivers without going through Neuron verbs.  
**Defense:** Bypasses staging/commit/outbox; dual bus.  
**Decision:** Forbidden. Host must not register grain factory into module-facing helpers as a supported pattern; Core drain owns factory use.

### G-O13 · ReadOnly without AlwaysInterleave is enough

**Attack:** Ship only ReadOnly on reads; skip AlwaysInterleave.  
**Defense:** Long model turns block journal observation for UI/introspection (sc03/04/live studio). v1 needed both.  
**Decision:** Pair both on Core read methods when observation-during-turn is required; never on mutators.

### G-O14 · OutboxWakeup as Neuron

**Attack:** One grain type for everything.  
**Defense:** Wakeup needs reentrancy and IRemindable — both forbidden on neurons.  
**Decision:** Separate reentrant helper grain. Correct as implemented.

### G-O15 · Analyzer-only seal without runtime checks

**Attack:** Roslyn analyzer is enough; drop activation/filter.  
**Defense:** Dynamic behaviors / future Kernel ALC can emit types analyzers miss; runtime is the contract.  
**Decision:** Runtime seal mandatory; analyzer optional later sugar.

---

## 8 · Mapping to package tree

```text
DigitalBrain.Core/
  Neuron*.cs              # DurableGrain body; sealed escapes
  NeuronConcurrency.cs    # activation seal
  Neuron.Schedule.cs      # timers ← module Schedule vocabulary
  OutboxWakeup.cs         # reminders ← idle backstop (non-Neuron)
  Filters/                # call filters + RequestContext headers
  Hosting/                # AddDigitalBrain, durable-key gate, wire filter
  Placement/              # OPTIONAL later — Core directors only
  Streams/                # OPTIONAL edge adapters only
  Offload/                # OPTIONAL later — non-Neuron SW hosts
```

No `DigitalBrain.Orleans` public package for modules. No re-export of Orleans namespaces from Core’s public API surface beyond what inheriting `Neuron` unavoidably exposes — and those members are sealed obsolete or internal.

---

## 9 · Stage gates

| Stage | Orleans seal obligations |
|---|---|
| **Stage 1 (now)** | Full NeuronConcurrency + filters + durable gate + Schedule/timer/reminder split + no streams required + transactions off + no SW neurons |
| **Stage 2** | Optional edge stream adapters with tests proving non-authority; optional placement directors with latency proof |
| **Stage 3** | Catalog epoch + Kernel Revision; still no module Orleans |
| **Any stage** | Opening module-visible Orleans types = failed seal → new grill required |

---

## 10 · Strongest objection & defense

**Objection:** Sealing Orleans throws away the platform; “real” Orleans apps let grains use the full surface; DigitalBrain becomes a restrictive actor framework nobody can extend.

**Defense:** Extension is **vocabulary and neurons**, not grain knobs. Full Orleans power is *present* (journaling, reminders, filters, timers, optional streams/SW/placement) where physics need it. The product trap list is almost entirely “module saw Orleans.” A sealed Core is how the framework stays small and the brain stays reconstructable. Power without a module name is still power.

**Fold condition:** Only if a shipping scenario cannot be expressed with Schedule + facts + DI + edge adapters — then extend **Core vocabulary**, not the module Orleans surface.

---

## 11 · RATIFIED summary table (copy-paste)

| Feature | Core | Module | Verdict |
|---|---|---|---|
| Serialized turns | Yes | Implicit | **Required** |
| Reentrant / MayInterleave (neuron) | No | No | **Forbidden** |
| AlwaysInterleave + ReadOnly | Read surface only | No | **Core surgical** |
| Call filters | Yes | No | **Core** |
| RequestContext | Envelope only | No | **Core transport** |
| DurableGrain / journaling | Yes | TState + verbs | **Core; raw sealed** |
| Timers | Drain + schedule | Schedule API | **Core how / module when** |
| Reminders | Wakeup grain | Schedule API | **Core how / module when** |
| Direct Deliver | Yes | Emit/Ask/Reply | **Core wire** |
| Streams explicit | Edge | No types | **Edge only** |
| Streams implicit | Ingress wake | No | **Ingress only** |
| Placement | Optional Core | No attrs | **Optional later** |
| Stateless worker | Non-neuron offload | DI service | **Optional; never Neuron** |
| Grain versioning | Fingerprint/epoch | Deploy/Revision | **Core+Kernel** |
| Transactions | No | No | **Forbidden** |
| Grain DI services | Host | Domain services yes | **Yes DI / no grain APIs** |

---

*Ratified after adversarial grill. Prefer delete. If a module needs an Orleans type, the design is wrong until proven otherwise in this file.*
