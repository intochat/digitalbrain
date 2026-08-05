# DigitalBrain Core — Stage-1 Constitution

**Status:** RATIFIED Stage-1 Core constitution.  
**Inputs:** `architecture-grill/01`–`11`, `scenarios/` (50).  
**Binding order:** this document · then `09-contradictions-resolved` FINAL LAW · then individual grills · then scenarios as expressibility tests.  
**Rule:** prefer delete; one mechanism per job; no type without a Stage-1 consumer.

---

## 0 · Status & how to read

| Layer | Authority |
|---|---|
| **This document** | Stage-1 Core shape: physics, four-type ABI, module/edge surface, sealed Orleans, package tree |
| **`architecture-grill/*`** | Adversarial decisions that produced the laws; amend laws only via §11 |
| **`scenarios/`** | Acceptance language — if Core cannot express a choreography, Core is wrong or the claim is Kernel/product |
| **Compiler + live journals** | Proof. Prose never greys a suite |

**Stage-1 ships** thin Core only. Kernel (behavior ALC, marketplace, capability gates, hot Revision), product modules (Gmail, Time UX, Flutter shell), and multi-owner IdP are **not** Core.

**One-line algebra:** hear (`INeuron<T>`) · say (`Emit`) · ask (`Ask`) · answer (return / `Reply` / deferred `TReply`) · wire (`Connect`/`Disconnect`) · later (`Schedule`/`Unschedule`) · heal (hear outcome facts) · edge (`Emit`/`Send`/`AskAsync` + `ReadAsync`).

---

## 1 · Physics (laws)

1. **Abstractions are exactly four types.** Module-visible ABI without inheriting `Neuron` lives in `DigitalBrain.Abstractions` only: `Synapse`, `INeuron<T>`, `NeuronId`, `SynapseMetadata`. Ceremony in the ABI is a bug.
2. **Orleans is Core’s body, never the module ABI.** Reentrancy, filters, RequestContext, DurableGrain/journaling, timers, reminders, placement, streams (edge only), versioning seams — all inside `DigitalBrain.Core`. Modules never import Orleans.
3. **One causal bus.** Neuron↔neuron delivery = journaled outbox + post-commit direct grain `Deliver`. Streams, pub-sub products, registries, and fire-and-forget are never authoritative n2n truth.
4. **Nothing leaves before commit.** Handler stages in memory; one batch write; return of `Deliver` means *committed*, never *the answer*. Handler throw → zero durable trace. Commit failure → poison + deactivate + reload committed truth. No retraction machinery.
5. **No neuron-awaits-neuron.** Continuations are later turns. Drain never runs inside the emitting handler. Same-turn reply ride-back is not a correctness path. Edge `AskAsync` observes the **session journal**.
6. **Causation is journal structure.** Public `SynapseMetadata` is identity only (Source, Sequence, Timestamp). Cause / Answers / To live on Core journal entries and read models. Modules never stamp lineage. Source is Core-minted from the emitting activation — unforgeable.
7. **Declarations define capability; connections define instance wiring.** Emit receivers = declared `INeuron<T>` at same context name **minus** ghost-suppressed kinds **∪** connection targets. Connections = durable table on the **emitter**, mutated only by journaled `Connect`/`Disconnect`. No remote registry on the emit path.
8. **Core owns all durable mutation in a turn.** One commit. Module-visible `WriteStateAsync`, raw `IDurable*`, `GrainFactory`, extra grain interfaces, `IRemindable`, raw timers — sealed away. Module state = one `TState` slot.
9. **At-least-once + watermark.** Dedup identity = `(Source, Sequence)`. Duplicate → silent success ack. FIFO per (sender, receiver); terminal hole commits before unblock. Terminal outcomes journal on the **sender** (`DeliveryFailed`, family). Never silent loss.
10. **Modules schedule facts; Core wakes the neuron.** Deferred self-delivery is Core schedule table + timer ticks + reminder backstop. Modules never see Orleans timers/reminders. Product “remind me” / cron / TZ / countdown = **Time module** on top of Schedule — not Core product types.
11. **Prefer delete.** Two mechanisms for one job → keep one. A type without a Stage-1 consumer is not shipped. Kernel is not Core.
12. **Proof is live or journaled.** No synthetic observations. Terminal failures are facts. Boot refusals are tested contracts. Depth budget (16, Core-private) bounds successful reaction storms; retry horizon bounds failed delivery.

---

## 2 · Abstractions (exactly 4 types)

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

| Type | Job |
|---|---|
| `Synapse` | Immutable fact body. Modules own sealed records under this root. |
| `INeuron<TSynapse>` | Declaration = subscription = address surface. Hearing is the behavior. |
| `NeuronId` | Durable address: string kind + context name. Never `System.Type` / AQN. |
| `SynapseMetadata` | Transport/read identity only. |

### Forbidden in Abstractions

`Synapse<TReply>` · dual `INeuron<TQ,TR>` · `Answer<,>` · `SynapseRef` · Cause/Answers/Correlation on metadata · Core synapses pack · `JournalFact` / `Delivery` / `NeuronReading` · `IDigitalBrain` / `IModule` · Orleans types · OwnerId / capability tokens · streams / `[WireTo]` · helpers for one sample.

**Rule:** if a type is only meaningful after commit or only produced by Core, it is not an Abstraction.

### Relocated (not deleted)

| Concern | Home |
|---|---|
| Ask typing, open asks | Core `IAnswers<,>`, `Ask`, edge `AskAsync` |
| Connect / outcomes / Schedule pack | Core public synapses |
| Cause / Answers / To | `JournalEntry` + `JournalFact` read model |
| Journal identity | Core `SynapseRef` |

---

## 3 · Module-visible Core surface

Package: `DigitalBrain.Core`. Modules subclass `Neuron` / `Neuron<TState>`, may implement `IAnswers<,>`, may listen to Core outcome facts. **No Orleans types in public module contracts.**

### 3.1 Verbs (in turn only)

| Verb | Resolution | Notes |
|---|---|---|
| `Emit(fact)` | Declared listeners @ `emitter.Name` **except** ghost kinds **∪** `connections[factKind]`; self skipped | Default speech. Speaker names nobody. `to: []` legal. |
| `Reply(fact)` | Turn **source** (Core-private) ∪ declared overhearers of fact type | Directed response; may stamp **Answers** when closing open ask. Return-from-answerer uses same path. |
| `Ask(question)` | Catalog **answerer kind** @ **asker.Name** | Open-ask pin; not Connect-able; requires continuation (`INeuron<TReply>` and/or `TState`). `Emit(question)` = announce only. Zero answerer → `DeliveryFailed`. |
| `Schedule(fact, period)` | Self schedule table by fact kind | Requires `INeuron<TFact>` for that type. First due = commit + period; period > 0. |
| `Unschedule<TFact>()` | Remove schedule row for kind | No-op if absent. |

**Absent Stage-1:** in-neuron `Send` · `Broadcast` API · raw timers · `IRemindable` · `WriteStateAsync` / `GrainFactory`.

### 3.2 IAnswers (Core, not Abstractions)

```csharp
public interface IAnswers<in TQuestion, TReply>
    where TQuestion : Synapse
    where TReply : Synapse
{
    Task<TReply?> HandleAsync(TQuestion question, CancellationToken cancellationToken);
}
```

- ≤1 answerer kind per question type (boot fail on 2+).
- Null return **defers**; later `Emit`/`Reply` of `TReply` closes open ask (Core stamps **Answers** on journal entry).
- Continuations: bare reply + `TState` / `INeuron<TReply>`. **No `Answer<>` reconstruction Stage-1.**

### 3.3 Core synapses (closed pack, Core assembly)

| Kind | Role | Module may |
|---|---|---|
| `Connect` / `Disconnect` | Instance wiring on emitter | Edge Send / Emit; reserved — no module `INeuron` hijack |
| `ConnectionRefused` | Bad Connect (typo, non-listener, question) | Hear |
| `DeliveryFailed` | Terminal outbox failure on **sender** | Hear / heal — **not** mint as transport truth |
| `AskExpired` | Open ask past horizon | Hear |
| `Schedule` / `Unschedule` | Remote same table as verbs | Emit; reserved intercept |
| `ScheduleFailed` | Consecutive tick failures → unscheduled | Hear / re-arm |

**Deleted forever:** `DeclaredRouteSurvives`, `Completed`, module-forged transport outcomes.

**Ghost rule:** connection target kind K for fact F suppresses same-context declared fan-out of F to K at that emitter.

### 3.4 Schedule policy (ratified — grill 01 + user correction)

| Who | Owns |
|---|---|
| **Module** | *What* fires and *when* in domain terms: `Schedule`/`Unschedule` verbs or facts; Time module for cron/TZ/snooze/NL “remind me” |
| **Core** | *How* the neuron wakes: schedule table, grain timers from table, `OutboxWakeup` (`IRemindable`) backstop for unsettled outbox **or** ask pins **or** schedules; tick = ordinary self-sourced turn |
| **Forbidden** | Module timers, module `IRemindable`, module-visible reminder grains, Core cron/countdown product API, outbox wakeup used as product scheduler, silent infinite tick retry |

One-shot idiom: schedule once → handler `Unschedule` + work. Cron idiom: module computes next UTC delay → `Schedule` → reschedule in handler.

### 3.5 Module composition

```csharp
silo.AddDigitalBrain(typeof(GmailIngress).Assembly, typeof(Chat).Assembly);
```

No `IModule`. Discovery = catalog over explicit types. Behavior **is** a neuron (Kernel owns compile/gate/epoch later). Stage-1 multi-owner = **one owner per deployment**.

---

## 4 · Edge surface (Brain / Session only)

Session **is** a neuron (`session/{context}`). Full journal / watermark / outbox. No edge ghost source. No `IDigitalBrain`.

```csharp
public sealed class Brain
{
    public Session Session(string context);

    public Task<NeuronReading> ReadAsync(
        NeuronId neuron,
        long afterPosition = 0,
        CancellationToken cancellationToken = default);
}

public sealed class Session
{
    public NeuronId Id { get; }

    public Task EmitAsync(Synapse fact, CancellationToken cancellationToken = default);

    public Task SendAsync(NeuronId receiver, Synapse fact, CancellationToken cancellationToken = default);

    public Task<TReply> AskAsync<TReply>(
        Synapse question,
        CancellationToken cancellationToken = default)
        where TReply : Synapse;
}
```

| Method | Returns when | Durable effect |
|---|---|---|
| `Session(context)` | Immediately | Addresses `session/{context}` |
| `EmitAsync` | After session turn **commit** | Said; declaration∪connection |
| `SendAsync` | After commit | Said; **exactly** named receiver — no fan-out |
| `AskAsync` | After `TReply` with Answers match **or** `DeliveryFailed`/`AskExpired` **or** cancel on **session journal** | Ask once; Task is volatile sugar |
| `ReadAsync` | Committed slice | None — journal + connections; Body null if kind unknown |

**Non-methods Stage-1:** `Watch*`, `Subscribe*`, `Get*`, `Activate*`, `Install*`, `Stream*`, UI/widget Core types.

UI = **module synapses** (`UiSurface`, chart specs, …) read as `JournalFact` bodies. Progressive UX = `ReadAsync` on work journals while long asks run. Multi-device = views on shared work context names; not per-device chat journals. Share pane = derived projection facts (modules), **never** raw owner journal dump APIs.

Stage-2 optional: `WatchAsync` = same cursor as `ReadAsync` (push may replace poll; never changes contracts).

---

## 5 · Internals

### 5.1 Orleans sealed matrix (summary)

| Feature | Core | Module sees |
|---|---|---|
| Serialized turns | Required | Implicit one turn |
| `[Reentrant]` / `[MayInterleave]` / `[StatelessWorker]` on Neuron | Forbidden | Boot refuse |
| `[AlwaysInterleave]` + `[ReadOnly]` | **Only** Core committed journal/state **reads** | Never on modules |
| Call filters + RequestContext | Envelope + interface whitelist; transport only | Never |
| DurableGrain + journaling | Entire durable surface | `TState` + verbs + `ReadAsync` |
| Grain timers | Drain pulse + schedule ticks | **Schedule only** |
| Reminders | `OutboxWakeup` companion grain | **Schedule only** |
| Direct `Deliver` | Sole n2n after commit | Emit/Reply/Ask/edge Send |
| Streams | Edge/ingress adapters only | No Orleans stream types |
| Placement | Optional later, Core/host | No module attrs |
| Stateless workers | Optional non-Neuron offload | DI service, never as Neuron |
| Grain versioning | Fingerprint; epoch hook Stage 3 | Deploy / Kernel Revision |
| Transactions | **Forbidden** | Fact sagas + TState |

**Seal layers:** package refs · obsolete `WriteStateAsync`/`GrainFactory`/`DeactivateOnIdle` · `NeuronConcurrency` activation · durable-key gate · incoming whitelist · outgoing self-proxy ban · catalog boot.

**Incoming whitelist (neuron):** `ITransport`, `IDrainEntry`, `ISessionEntry`, `Orleans*` runtime. Everything else = second wire.

### 5.2 Journal pipeline

**Durable keys (complete set):** `journal`, `journal.sequence`, `outbox.cursor`, `outbox.progress`, `asks`, `asks.open`, `dedup`, `connections`, `schedule`, `tallies.heard`, `tallies.said`, `state`.

**Turn (receiver):** Deliver → watermark dedup → reserved intercept → open turn (no durable mutate) → handler stages memory → stage batch (heard, said+receiver snapshot, state, watermark, asks, schedule, Answers last) → **arm wakeup before write** → one `WriteStateAsync` → MarkCommitted / drain timer **or** poison.

**Dispatch (separate turns):** Drain timer/reminder → rehydrate from journal → per-receiver FIFO + blocked targets → terminal `DeliveryFailed` commit before unblock → cursor/compact under hard floor `min(cursor, oldest ask pin)`.

**Bounds (`DeliveryPolicy`):** attempts, retry horizon, attempt timeout, drain interval, wakeup cadence, ask horizon (2× retry), watermark retention, compaction soft targets, schedule failure limit, **MaximumDepth = 16** (storm control, Core-private, not delivery identity).

### 5.3 Catalog

`Catalog.Build(types)` — pure, one reflection pass, per-silo DI. Kind = lowercased class name; collision fails boot. Listeners exact `INeuron<T>`; answerers exact `IAnswers<,>` (≤1). Fingerprint = SHA-256 of sorted hear/answer/continue rows; silos must match. Reserved kinds not module-listenable. No dual derivation (no source-gen table + reflection). Stage-1 N+1 = redeploy composition; hot epoch = Kernel Stage 3.

### 5.4 Filters

- Incoming: whitelist + envelope consume on delivery.
- Outgoing: self-proxy throw; envelope write on delivery.
- Wire types: Core grain interfaces only; speech = Synapse JSON.

---

## 6 · Package tree

```text
src/
  DigitalBrain.Abstractions/     # ZERO deps — 4 types only
    Synapse.cs · INeuron.cs · NeuronId.cs · SynapseMetadata.cs

  DigitalBrain.Core/             # Orleans body; sealed from modules
    Neuron.cs · NeuronOfState.cs · Neuron.Dispatch.cs · Neuron.Asks.cs
    Neuron.Connections.cs · Neuron.Schedule.cs · Neuron.Transport.cs
    NeuronConcurrency.cs · NeuronJournal.cs · JournalEntry.cs · BodyCodec.cs
    Catalog.cs · DeliveryPolicy.cs · OutboxWakeup.cs · NeuronTime.cs
    CoreSynapses.cs · IAnswers.cs · Brain.cs · JournalReading.cs
    Filters/ · Hosting/AddDigitalBrain
    Streams/ · Placement/ · Offload/   # Stage-2 optional; edge/offload only

  DigitalBrain.Testing/          # real clusters, clocks, commit faults, journal asserts

  DigitalBrain.Kernel/           # LATER — ALC, Revision, capability, marketplace client

modules/*                        # sealed facts + Neuron subclasses; never Orleans public
samples/
```

**Non-packages:** no `DigitalBrain.Bus`, no n2n streaming product surface, no `Abstractions.Answers`.

---

## 7 · What Core is not

| Not Core | Owner |
|---|---|
| Fat Abstractions / dual listener / `Answer<>` ABI | Deleted |
| `IDigitalBrain` mega-interface | Deleted / product host only outside Core |
| Streams as n2n bus; global timeline | Forbidden |
| Emit-path registry / dual topology derivation | Forbidden |
| Neuron-await-neuron; same-turn reply as truth | Forbidden |
| Module timers, `IRemindable`, raw `IDurable*`, GrainFactory | Forbidden |
| Core cron / countdown / “Reminder” product types | Time **module** |
| Roslyn / ALC / marketplace / signed packages | Kernel |
| Multi-owner keys in `NeuronId` | Stage-1: deployment isolation |
| Share-of-journals API; prompt-injection policy | Modules / edge |
| Orleans Transactions; hop depth as identity | Forbidden / storm budget only |
| Behaviors as second VM or RPC veneer | Behaviors are neurons |
| Faked proofs; synthetic journal observations | Forbidden |

---

## 8 · Scenario fitness (50 → Core capability)

| # | Theme | Core capability | Not Core |
|---|---|---|---|
| 01 | Gmail→web→CRM enrich | Emit chain, journals | Module IO |
| 02 | X→crypto dashboard | Emit + Connect rewire | UI product |
| 03 | Weekly recall | `ReadAsync`, tallies | Introspection module |
| 04 | Why this way | Journal Cause structure | — |
| 05 | C# behavior install | Neuron + Connect + catalog hook | Kernel ALC |
| 06 | Rich chat image/chart | Session + multimodal **module** facts | Flutter widgets |
| 07 | Multi-tool + approval | Ask/open ask; domain approval facts | UI gate product |
| 08 | Calendar conflict email | Multi-module Emit/Ask | Calendar/Gmail modules |
| 09 | One correlation thread | Journal Cause/Answers | — |
| 10 | Live dashboard stream | Emit fan-out; edge journal read/mirror | Stream as authority |
| 11 | Voice→tasks+calendar | Ingress facts + Emit | STT product |
| 12 | MCP/IDE federation | Edge facts | MCP host product |
| 13 | Multi-device handoff | Shared work context journals | Placement S2 |
| 14 | Legal hold | Journals as truth | Compliance product |
| 15 | Travel multi-approval | Multi-turn facts, open asks | Saga product |
| 16 | Invoice OCR→pay | Pipeline Emit/Ask | Module IO |
| 17 | Team standup | Fan-in via TState/journal | — |
| 18 | Deal-close email seq | Multi-turn + Schedule optional | Gmail module |
| 19 | Live widget+behavior | Neuron + Connect; UI facts | Kernel Studio |
| 20 | Research+citations | Multi-turn + journals | — |
| 21 | Meeting→action fan-out | Emit broadcast | — |
| 22 | Wallet tax journal | Journals | Crypto module |
| 23 | Churn alert cascade | Emit + heal patterns | — |
| 24 | Hot-reload in-flight asks | Fingerprint hook only | Kernel epoch S3 |
| 25 | Owner isolation | Deployment isolation | Shared-silo multi-tenant |
| 26 | Hot-reload live traffic | Same as 24 | Kernel epoch |
| 27 | Multi-owner | Deployment isolation | Kernel IdP |
| 28 | Implicit stream wake | Ingress adapter → journal first | Stream as catalog |
| 29 | Long research progressive UI | Multi-turn + ReadAsync progress | Holding one turn |
| 30 | Cancel/replan mid-stream | Domain facts + Unschedule | Distributed rollback |
| 31 | Social→stop-loss | Emit + Schedule recheck | Price firehose = ingress |
| 32 | Notes→tasks→Slack | Emit pipeline + Connect | — |
| 33 | Whiteboard photo→tasks | Multimodal module facts | Vision module |
| 34 | Replay last Tuesday | Journal read / compaction floor | — |
| 35 | DeliveryFailed self-heal | `DeliveryFailed` listenable | — |
| 36 | Script reacts to all email | Behavior-as-neuron + Connect | Kernel install |
| 37 | Nested asks | Ask + TState/INeuron reply; no grain await | Answer reconstruction |
| 38 | Chart+image+buttons | Module UI facts | Core RichMessage |
| 39 | Nightly batch | Schedule + module next-due math | Core cron |
| 40 | Voice→CRM+email | Ingress + Emit/Ask | — |
| 41 | OAuth mid-workflow | Deferred asks | Token vault product |
| 42 | Share pane not journals | Per-neuron journals; guest session Name | ShareGateway module |
| 43 | Prompt injection via email | Unforgeable Source; body = data | TrustTagger/EgressGate |
| 44 | Rolling grain version | Fingerprint refuse | Orleans impl version / epoch |
| 45 | 10k embeddings | Orchestrator neuron | Stateless worker S2 offload |
| 46 | 30d dormant reminder | Schedule table + reminder backstop | Product countdown chrome |
| 47 | Many dashboards | Emit pub-sub + Connect instances | Producer address book |
| 48 | Why sales dropped | Journals + multi-chart facts | — |
| 49 | Marketplace N+1 handlers | Declaration fan-out; fingerprint | Marketplace/Kernel |
| 50 | Morning brief day-in-life | Schedule + multi-module Emit/Ask | Time UX module |

**Collective claims:** thin ABI · journal = truth · broadcast = Emit resolution · streams = edge · behaviors = neurons · no neuron-await · DeliveryFailed first-class · Schedule wakes dormant work · rich UI = module facts · catalog N+1 · isolation = deployment.

---

## 9 · Proof obligations

Do not claim Stage-1 green without (public API / cluster tests in `DigitalBrain.Core.Tests`):

| ID | Assertion |
|---|---|
| P1 | Abstractions public surface = **exactly four** types |
| P2 | Greeter: edge `AskAsync` + Answers stamp; no `Answer<>` type |
| P3 | Emit zero-receiver journals `to: []` |
| P4 | Connect then Emit: only connected instance; ghost kind suppressed |
| P5 | Bad Connect → `ConnectionRefused`; no `DeclaredRouteSurvives` |
| P6 | Ask routes to answerer @ name; `Emit(question)` does not |
| P7 | Answer return and deferred TReply both reach asker |
| P8 | Nested asks complete via later turns + TState (no nested grain await) |
| P9 | Handler throw → zero durable trace; commit fault → poison reload |
| P10 | Redelivery watermark swallows; no dual handler run |
| P11 | `DeliveryFailed` on sender after terminal; listenable heal |
| P12 | Schedule tick after deactivation (sc46 shape); `ScheduleFailed` after limit |
| P13 | `AskExpired` on horizon; late reply no continuation |
| P14 | Session `Send` does not declaration-fan-out |
| P15 | No streams required for n2n; root gate green without stream bus |
| P16 | Module type with `IRemindable` / extra grain iface / Reentrant fails boot |
| P17 | Proxied self-call throws; self-delivery direct path works |
| P18 | Said Source = emitter Id; no module Source API |
| P19 | Depth > 16 → attempt-1 `DeliveryFailed` (when depth lands) |
| P20 | Catalog fingerprint mismatch refuse path (join gate as hosted) |

---

## 10 · Build order

Each step ends green under Core tests. No red root gate. No feature without a consumer test in the same change.

1. **Reset Abstractions** to four types; relocate Core pack, read models, `SynapseRef`; thin metadata.
2. **Core `IAnswers<,>`** + journal Cause/Answers (not public envelope).
3. **Turn vertical slice** — Deliver → handle → stage → one commit → poison (no drain).
4. **Journal-as-outbox drain** — watermark, FIFO, `DeliveryFailed`, arm-before-commit, wakeup.
5. **Emit resolution** — catalog declarations + same-context fan-out.
6. **Connections** — Connect/Disconnect, ghost rule, `ConnectionRefused`.
7. **Ask / Reply / edge AskAsync** — open asks, defer, AskExpired; greeter.
8. **Schedule** — verbs + facts + `ScheduleFailed`; dormant wake test.
9. **Brain/Session + ReadAsync** — causal reconstruct test.
10. **Catalog fingerprint** multi-silo contract.
11. **Depth budget** + seal tests (concurrency, filters, provenance).
12. **Stop.** No Kernel ALC, no transactions, no `IDigitalBrain`, no stream n2n, no Answer reconstruction.

Optional Stage-2: edge stream adapters (non-authority), `WatchAsync`, placement, non-Neuron SW offload, in-neuron Send (Kernel consumer only).

---

## 11 · Change control

| Change | Required |
|---|---|
| Amend a **physics law** (§1) | New `architecture-grill/` document: claim → strongest attack → defend/fold → stamp; update this constitution in the same change |
| Add a type to **Abstractions** | Almost never. Must prove it is host-agnostic and module-named without Core. Grill required |
| Add Core synapse / verb | Grill + scenario consumer + proof test; closed pack append-only discipline |
| Open module-visible **Orleans** type | Failed seal — grill 05 amendment mandatory |
| Stage-2 feature | Named consumer + “cannot express as fact/DI/edge” proof; stay off Stage-1 inventory |
| Delete-pass conflict | Prefer **delete**; thicker tree snapshot loses to this constitution |

**Do not silent-patch** durable structure lists, turn pipeline order, or forbidden tables in grill `03`/`07` without a new grill stamp.

---

*Prefer delete. Modules schedule facts; Core owns waking the turn; Orleans timers/reminders are never a module API; product reminders are a Time module. One bus. Four types. Proof is the journal.*
