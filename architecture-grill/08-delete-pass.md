# 08 · Delete pass — ruthless Stage-1 Core surface

Date: 2026-08-05. Role: Core simplifier. Method: delete-first grill against
`CORE-ARCHITECTURE.md` (ratified), `scenarios/` (50 stress cases), and the **current tree**
under `src/DigitalBrain.Abstractions` + `src/DigitalBrain.Core` (which is **fatter than
ratified** and is the deletion target).

**Bar:** every scenario’s **Synapse choreography** must remain expressible. Product modules
(Gmail, crypto UI, Kernel ALC, OAuth, marketplace) are not Core. Prefer one mechanism per
job. A type with no Stage-1 consumer is not Stage-1.

---

## 0 · Ground truth gap (code vs ratified)

| Concern | Ratified (`CORE-ARCHITECTURE.md`) | Tree today | Delete-pass stance |
|---|---|---|---|
| Abstractions count | Exactly 4 | 9 files: `Synapse`(+`Synapse<>`), dual `INeuron`, `Answer<>`, `CoreSynapses`, `JournalFact`/`Delivery`/`NeuronReading`, `SynapseRef`, fat `SynapseMetadata` | **Reset to 4** |
| Answer protocol | Core `IAnswers<,>` | Abstractions `INeuron<TQ,TR>` + `Synapse<TReply>` | **IAnswers in Core**; kill dual listener interface from ABI |
| Continuations | Bare reply + `TState`; no Stage-1 `Answer<>` | Full `Answer<,>` reconstruct + shape fingerprint | **Delete reconstruction Stage-1** |
| Core vocabulary | Core assembly | In Abstractions (`CoreSynapses.cs`) + `DeclaredRouteSurvives` in Core | **Core only**; shrink pack |
| Metadata | Identity only (Source, Sequence, Timestamp) | Cause + Answers on public `SynapseMetadata` | **Thin public**; Cause/Answers stay on journal / read model |
| `Reply` verb | Documented | **Missing** (answer = return from answerer / Emit closes open ask) | **Stage-1 MUST add** directed Reply (or document Emit-only and delete the verb name) |

This pass **ratifies the thinner architecture**, not the thicker branch snapshot.

---

## 1 · Self-grill (targeted challenges)

### G-D1 · Schedule in Core

**Claim to kill:** domain schedule (pulse facts, morning brief, 30-day reminder) is Kernel or
module `TState` + raw Orleans reminders; Core keeps only outbox wakeup.

**Strongest argument to kill it**

- Outbox reminder grain already wakes dormant neurons with unsettled work.
- Scenarios 39/46/50 are “time happened”; modules could hold `NextDue` and get a generic
  `Tick` from Kernel.
- Schedule table, remote `Schedule`/`Unschedule` facts, tick Cause wiring, consecutive-failure
  `ScheduleFailed` is a second durable subsystem beside the journal-outbox.

**Defense (why it stays Stage-1)**

- Modules **must not** see `IRemindable`, grain timers, or `GrainFactory` (physics). Without a
  Core schedule table, every time-bearing module reinvents durable arm/disarm and loses
  “scheduled fact is journaled speech.”
- Forced by scenarios: **39** nightly batch, **46** 30-day dormant wake, **50** morning brief
  due, **02** sampling windows, **35** delayed retry pulse, **45** chunk retry. That is not
  one demo — it is the time half of the nervous system.
- One mechanism: in-turn verbs and remote `Schedule`/`Unschedule` facts write the **same**
  table; ticks re-enter ordinary `Deliver` as self-sourced heard entries.

**Fold or stand:** **STAND — Schedule stays Stage-1 Core.**

**Thinning that still stands**

- Period-only is enough Stage-1 (one-shot = fire then `Unschedule` / remove row). No cron DSL.
- No separate “Scheduler” system grain; table lives on the target neuron.
- Do **not** promote Schedule into Abstractions.

---

### G-D2 · `IAnswers` vs dual `INeuron` / `Synapse<TReply>`

**Claim to kill:** typed ask/answer needs `Synapse<TReply>` and `INeuron<TQ,TR>` on the ABI so
modules never reference Core for answering.

**Strongest argument to keep dual ABI**

- Edge `AskAsync` inference and boot “one answerer per question” read cleanly off
  `Synapse<TReply>`.
- One interface family (`INeuron`) for hear and answer feels smaller than two names.

**Defense (why dual ABI dies)**

- Ratified G1/G12: reply pairing is a **protocol**, not a second fact-root and not a second
  listener shape in the ABI.
- Dual dispatch paths (`DeliverAsync` vs `DeliverQuestionAsync`) are Core’s problem either way;
  putting the fork in Abstractions freezes it as public language.
- Behavioral modules already depend on `Neuron` (Core). Pure vocabulary packs never answer.

**Fold or stand:** **DELETE forever from Abstractions:** `Synapse<TReply>`, `INeuron<TQ,TR>`.
**Stage-1 MUST in Core:** `IAnswers<in TQuestion, TReply>` (exact one answerer kind per
question type at boot). Edge: `AskAsync<TReply>(Synapse question)` (type arg on the method).

---

### G-D3 · Connect

**Claim to kill:** declaration fan-out + edge `Session.SendAsync(to, fact)` is enough; Connect
is Subscribe-with-a-new-name.

**Strongest argument to kill Connect**

- Same-context `INeuron<T>` already is broadcast.
- Directed edge Send covers “this instance only.”
- Ghost rule + connection dictionary + `ConnectionRefused` + `DeclaredRouteSurvives` is pack
  weight (G6 residual risk).

**Defense**

- North-star and behaviors need **instance rewiring without redeploy** (sc **02**, **05**,
  **36**, **32**, **47** optional Connect). Redeploy-only topology kills “behavior is a
  neuron the brain can rewire.”
- Connections are **local durable state on the emitter**, journaled via ordinary bus facts —
  not emit-path registry RPC (that is what killed v1 Subscribe).
- Ghost rule is the price of not running dual pipelines at one name.

**Fold or stand:** **STAND — Connect/Disconnect Stage-1 MUST.**

**Thinning:** **DELETE forever `DeclaredRouteSurvives`.** Disconnect success + connection
table + catalog already answer “declaration resumes.” No scenario names that fact; it is
honesty cosplay.

---

### G-D4 · Answer reconstruction

**Claim to kill:** `Answer<Q,R>` dispatch view + journal rehydrate of the original question +
shape fingerprint.

**Strongest argument to keep it**

- FLOWS.md flow 3 “Continue without state” is prettier: handler gets `(Question, Reply)`.
- Nested asks (sc **37**, **07**, **48**) read as Answer language in scenario prose.

**Defense (why Stage-1 deletes it)**

- Reconstruction is Core cost, shape-drift machinery, and a parallel fact type that must never
  be journaled or Emit’d — already special-cased everywhere.
- Nested workflows are multi-turn **facts** + **at most one open ask per question kind** +
  join in `TState` or `INeuron<TReply>`. That is enough for every scenario that says “Answer.”
- Ratified G12: measure ceremony in real BDD before growing Core; do not ship reconstruction
  “because FLOWS imagined it.”

**Fold or stand:** **DELETE Stage-1 (and forever from Abstractions).** Stage-2 **only if**
live module ceremony tax is measured and a **Core-only** (non-journaled) dispatch view earns
its keep. Continuations Stage-1:

1. Answerer returns `TReply?` (null = defer open ask).
2. Later `Emit`/`Reply` of matching `TReply` stamps journal **Answers** and delivers to asker.
3. Asker declares `INeuron<TReply>` and/or keeps join keys in `TState`.

Catalog drops: `Answer<,>` continuation rows, shape fingerprints for reconstruction, ask-time
`HasContinuation` requiring `INeuron<Answer<…>>`. Optional later: require asker to declare
`INeuron<TReply>` **or** accept “orphan reply is journaled reception only” (session already
does this).

---

### G-D5 · CoreSynapses pack size

**Current sealed kinds (tree):**  
`Connect`, `Disconnect`, `ConnectionRefused`, `DeliveryFailed`, `AskExpired`, `Schedule`,
`Unschedule`, `ScheduleFailed`, `DeclaredRouteSurvives`.

| Kind | Stage-1 | Rationale |
|---|---|---|
| `Connect` | **MUST** | Instance topology (G-D3) |
| `Disconnect` | **MUST** | Pair of Connect; idempotent remove |
| `ConnectionRefused` | **MUST** | Loud door for typos / non-listeners (sc35) |
| `DeliveryFailed` | **MUST** | sc **35**; self-heal is listenable physics |
| `AskExpired` | **MUST** | Nested/long asks (sc **37**, **07**, **48**, **50** partial join) |
| `Schedule` | **MUST** | G-D1 |
| `Unschedule` | **MUST** | Same table; cancel/replan (sc **30**, **46**) |
| `ScheduleFailed` | **MUST** | Terminal honesty; no silent infinite tick death |
| `DeclaredRouteSurvives` | **DELETE forever** | Reducible to table + catalog |
| `Completed` (CONTEXT.md) | **DELETE forever** | Null defer + no reply is enough; not in ratified Core pack |
| Any module-named “Core” outcome | **DELETE forever** | Modules own product facts |

**Pack location:** **Core assembly only** (ratified G2). Modules that heal delivery reference
Core. Not Abstractions.

**Reserved interception set (module `INeuron<>` banned):**  
`Connect`, `Disconnect`, `Schedule`, `Unschedule`. Outcomes stay ordinary listenable kinds.

---

### G-D6 · Multiple partial `Neuron` files

**Files today:** `Neuron.cs`, `Neuron.Dispatch.cs`, `Neuron.Asks.cs`, `Neuron.Connections.cs`,
`Neuron.Schedule.cs`, `Neuron.Transport.cs`, `NeuronOfState.cs`.

**Claim to kill:** partials are ceremony; one god file or fewer.

**Grill**

- Partials are **not conceptual surface**; they are navigation. Public concepts are verbs +
  types, not file count.
- `Neuron.Asks.cs` is tiny (expiry only) — fold into Dispatch when touching that area.
- Connections + Schedule both intercept reserved kinds — optional single
  `Neuron.Interception.cs` later.

**Decision:** **Stage-1 keep responsibility split** (or merge only as pure refactor with no
API change). **Do not** treat file merge as product progress. **DELETE forever** any urge to
expose partials or “extension points” as module API.

---

### G-D7 · Fat public metadata / journal identity types in Abstractions

**Delete from Abstractions forever:** `SynapseRef`, `JournalFact`, `Delivery`, `NeuronReading`,
Cause/Answers on `SynapseMetadata`, `Answer<>`, Core pack, `Synapse<TReply>`, dual `INeuron`.

**Stage-1 homes**

| Type | Home |
|---|---|
| `SynapseRef` | Core public (refs in Core synapses + edge) |
| `JournalFact`, `Delivery`, `NeuronReading` | Core public read models (`Brain.ReadAsync`) |
| Cause / Answers | Durable `JournalEntry` + **fields on `JournalFact`**, not author-stamped metadata |
| `SynapseMetadata` (public) | Source, Sequence, Timestamp only |

Edge ask matching keys off **journal** Answers stamps (and Core synapses), not module
Cause-scanning.

---

## 2 · Scenario coverage without the deleted mass

| Scenario class | IDs | Stage-1 Core enough? | What is *not* Core |
|---|---|---|---|
| Enrichment / multi-tool pipelines | 01, 06–08, 11, 16, 18, 20–23, 31–33, 40 | Emit + Ask + journal join + outbox | Module IO, OAuth product |
| Social → dashboard / pub-sub | 02, 10, 47 | Emit broadcast + Connect + journals | Edge stream/SSE **adapter** (Stage-2 polish; poll/journal OK Stage-1) |
| Recall / why | 03, 04, 09, 34, 48 | `ReadAsync`, Cause on journal entries | Introspection **module** |
| Behaviors / install | 05, 19, 24, 26, 36, 49 | Neuron + Connect + catalog fingerprint hook | Kernel ALC, marketplace, hot epoch |
| Approval / cancel | 07, 15, 30 | Facts + open asks + Unschedule | UI product |
| Isolation / share | 25, 27, 42 | One owner per deployment | Multi-tenant IdP |
| Ingress wake | 28 | Edge adapter → first journaled Deliver | Implicit stream bus as n2n truth |
| Progressive / long | 29, 30 | Multi-turn + TState + AskExpired | Holding one turn forever |
| DeliveryFailed heal | 35 | DeliveryFailed + optional Schedule retry | — |
| Nested asks | 37 | Ask + INeuron\<reply\> / TState | Answer reconstruction |
| Nightly / reminder / brief | 39, 46, 50 | Schedule table + reminders | — |
| Embeddings 10k | 45 | Orchestrator neuron + fan-out asks | Stateless **worker** grain Stage-2 |
| Version roll | 44 | Fingerprint refuse | Grain version / Revision epoch Stage-2/3 |
| Adversarial / compliance | 14, 43 | Journals + filters; no dual bus | Legal-hold **product** policy |
| Multimodal UI | 06, 33, 38 | UI facts as synapses | Flutter semantics |
| MCP / federation | 12 | Facts at edge | MCP host product |
| Multi-device | 13 | Session context names | Placement tuning Stage-2 |
| OAuth mid-flow | 41 | Deferred asks + module secrets | Token vault product |

**None of the 50 require:** `Answer<>` reconstruction, `DeclaredRouteSurvives`, fat
Abstractions, streams-as-n2n-bus, `IDigitalBrain`, transactions, dual `INeuron` in ABI,
module-visible Orleans.

---

## 3 · Inventory: every type / verb / file

Legend: **S1** = Stage-1 MUST · **S2** = Stage-2 (or Kernel) · **DEL** = delete forever
(or never ship).

### 3.1 · Abstractions (`DigitalBrain.Abstractions`) — zero deps

| Item | File today | Verdict | Notes |
|---|---|---|---|
| `Synapse` | `Synapse.cs` | **S1** | Sole fact root |
| `Synapse<TReply>` | `Synapse.cs` | **DEL** | Pairing → Core `IAnswers` / method type args |
| `INeuron<T>` | `INeuron.cs` | **S1** | Hearing = subscription |
| `Hear` default | `INeuron.cs` | **S1** | Cheap; bodiless Diary (sc planner path) |
| `INeuron<TQ,TR>` | `INeuron.cs` | **DEL** (from ABI) | Replaced by Core `IAnswers` |
| `NeuronId` | `NeuronId.cs` | **S1** | `Kind` + `Name`; `KindOf(Type)` |
| `SynapseMetadata` thin | `SynapseMetadata.cs` | **S1** | Source, Sequence, Timestamp **only** |
| Cause/Answers on metadata | `SynapseMetadata.cs` | **DEL** (public envelope) | Live on journal / `JournalFact` |
| `Answer<,>` | `Answer.cs` | **DEL** Stage-1 + forever from ABI | Optional S2 Core-only view |
| `SynapseRef` | `SynapseRef.cs` | **DEL** from Abstractions | **S1** in Core |
| `JournalFact` / `Delivery` / `NeuronReading` | `JournalFact.cs` | **DEL** from Abstractions | **S1** in Core |
| Core pack records | `CoreSynapses.cs` | **DEL** from Abstractions | **S1** in Core (shrunk) |
| Entire `CoreSynapses.cs` path in Abstractions | file | **DEL** | Relocate |

**Ratified Abstractions = 4 types:** `Synapse`, `INeuron<T>`, `NeuronId`, `SynapseMetadata`.

### 3.2 · Core public vocabulary (synapses)

| Kind | Verdict |
|---|---|
| `Connect` | **S1** |
| `Disconnect` | **S1** |
| `ConnectionRefused` | **S1** |
| `DeliveryFailed` | **S1** |
| `AskExpired` | **S1** |
| `Schedule` | **S1** |
| `Unschedule` | **S1** |
| `ScheduleFailed` | **S1** |
| `DeclaredRouteSurvives` | **DEL** |
| `Completed` | **DEL** |

### 3.3 · Core public types / edge

| Item | File | Verdict |
|---|---|---|
| `Neuron` | `Neuron.cs` (+ partials) | **S1** |
| `Neuron<TState>` | `NeuronOfState.cs` | **S1** |
| `IAnswers<,>` | (new; not dual `INeuron`) | **S1** |
| `Brain` | `Brain.cs` | **S1** |
| `Session` (edge client) | `Brain.cs` | **S1** |
| `Session` grain (kind `session`) | `Brain.cs` nested | **S1** |
| `SynapseRef` | relocate | **S1** |
| `JournalFact`, `Delivery`, `NeuronReading` | relocate | **S1** |
| `AskFailedException` | `Brain.cs` | **S1** |
| `AskOutcomeUnknownException` | `Brain.cs` | **S1** |
| `UnhandledFactException` | `UnhandledFactException.cs` | **S1** |
| `IDigitalBrain` facade | — | **DEL** |
| `IModule` mega-interface | — | **DEL** |
| Streams product surface / second bus | — | **DEL** |
| `ReadStateAsync` edge | `Neuron.Transport.cs` | **S2** (optional); journals first |

### 3.4 · Core verbs (module-facing)

| Verb | Verdict | Notes |
|---|---|---|
| `Emit(fact)` | **S1** | Declaration ∪ connection resolution |
| `Ask` / `Ask<TReply>(…)` | **S1** | Open ask + answerer route; no neuron-await |
| `Reply(fact)` | **S1** | Directed to turn source; Core-private envelope — **implement if missing** |
| `Schedule(fact, period)` | **S1** | Same table as remote fact |
| `Unschedule<TFact>()` | **S1** | |
| In-neuron `Send(to, fact)` | **S2** | Edge `Session.SendAsync` is S1; in-neuron only when Kernel consumer forces |
| `Broadcast` second API | **DEL** | Emit *is* broadcast |
| Module `WriteStateAsync` / `GrainFactory` / raw timers | **DEL** | Sealed obsolete |

### 3.5 · Core edge session verbs

| Verb | Verdict |
|---|---|
| `Session.EmitAsync` | **S1** |
| `Session.SendAsync` | **S1** |
| `Session.AskAsync` | **S1** |
| `Brain.ReadAsync` | **S1** |
| `Brain.Get<TNeuron>` send/ask | **DEL** | Already rejected |

### 3.6 · Core internal runtime (must exist; not module vocabulary)

| Item | File | Verdict |
|---|---|---|
| Catalog build + fingerprint | `Catalog.cs` | **S1** |
| Journal-as-outbox, watermark, progress, pins, connections, schedule, state slot, tallies | `NeuronJournal.cs` | **S1** (tallies soft-compaction) |
| `JournalEntry` + closed entry records | `JournalEntry.cs` | **S1** |
| Drain, FIFO, DeliveryFailed emit, arm-before-commit | `Neuron.Dispatch.cs` | **S1** |
| Connect/Disconnect intercept | `Neuron.Connections.cs` | **S1** |
| Schedule intercept + ticks | `Neuron.Schedule.cs` | **S1** |
| Ask expiry | `Neuron.Asks.cs` | **S1** (may fold into Dispatch) |
| Transport Deliver / question route / Read | `Neuron.Transport.cs` | **S1** |
| BodyCodec | `BodyCodec.cs` | **S1** |
| DeliveryPolicy bounds | `DeliveryPolicy.cs` | **S1** |
| OutboxWakeup reminder grain | `OutboxWakeup.cs` | **S1** |
| NeuronConcurrency / serialized turns | `NeuronConcurrency.cs` | **S1** |
| Filters + SynapseHeaders | `Filters/*` | **S1** |
| `AddDigitalBrain` hosting | `Hosting/DigitalBrainSiloExtensions.cs` | **S1** |
| NeuronTime keyed clock | `NeuronTime.cs` | **S1** |
| Answer reconstruction / shape fingerprint continuations | Catalog + Transport | **DEL** Stage-1 |
| Dual question wire forever | Transport | **S1 keep mechanism** under `IAnswers`; not ABI |
| Streams/ edge adapters folder | — | **S2** |
| Placement strategies | — | **S2** |
| Stateless worker host helper | — | **S2** (never as Neuron) |
| Catalog hot Revision / grain versioning product | — | **S2/S3 Kernel** |
| Orleans Transactions | — | **DEL** default |
| Global timeline stream | — | **DEL** |
| Multi-owner keys in `NeuronId` | — | **DEL** Stage-1 |

### 3.7 · Testing package (support, not Core concepts)

| Item | Verdict |
|---|---|
| Real cluster fixtures, clocks, journal asserts, commit faults | **S1** |
| Dual “Core without Orleans” abstraction layer | **DEL** |

### 3.8 · File-level disposition (Core + Abstractions)

| Path | Verdict |
|---|---|
| `Abstractions/Synapse.cs` | **S1** — strip `Synapse<>` |
| `Abstractions/INeuron.cs` | **S1** — single arity only |
| `Abstractions/NeuronId.cs` | **S1** |
| `Abstractions/SynapseMetadata.cs` | **S1** — thin |
| `Abstractions/Answer.cs` | **DEL** |
| `Abstractions/CoreSynapses.cs` | **DEL** (content → Core) |
| `Abstractions/JournalFact.cs` | **DEL** (content → Core) |
| `Abstractions/SynapseRef.cs` | **DEL** (content → Core) |
| `Core/Neuron*.cs` partials | **S1** runtime; merge = refactor only |
| `Core/Neuron.Connections.cs` `DeclaredRouteSurvives` | **DEL** type |
| `Core/CoreSynapses.cs` (new) | **S1** shrunk pack |
| `Core/IAnswers.cs` (new) | **S1** |
| `Core/JournalReading.cs` (new name ok) | **S1** public read models |
| `Core/Streams/*` | **S2** |
| `Core/Placement/*` | **S2** |

---

## 4 · Mechanisms reduced to one

| Job | One mechanism | Forbidden second |
|---|---|---|
| n2n delivery | Journaled said + direct `Deliver` | Streams bus, fire-and-forget |
| Fan-out | Emit resolution = declaration ∪ connection | `Broadcast` API, global timeline |
| Instance route | Emitter connection table via Connect facts | Emit-path registry, string `[WireTo]` |
| Ask/answer | `IAnswers` + open ask + Answers stamp | Neuron-awaits-neuron, same-turn ride-back truth |
| Continuation | `INeuron<TReply>` and/or `TState` | `Answer<>` reconstruct Stage-1 |
| Time pulse | Schedule table + timer + reminder backstop | Module `IRemindable`, second scheduler grain |
| Failure | `DeliveryFailed` / `AskExpired` / `ScheduleFailed` / `ConnectionRefused` | Silent drop, synthetic observations |
| Causation | Journal entry structure | Author Cause API on metadata |
| Edge speech | Session Emit/Send/Ask + Read | `IDigitalBrain`, `Get<TNeuron>` |
| Module state | `TState` one slot | Module `IDurable*`, extra grain ifaces |

---

## 5 · RATIFIED minimal Stage-1 Core inventory

**Remaining concepts only** (no file poetry, no Stage-2):

### Abstractions (exactly four)

- **`Synapse`** — immutable fact body root  
- **`INeuron<TSynapse>`** — declaration = subscription = hear  
- **`NeuronId`** — durable address `(Kind, Name)`  
- **`SynapseMetadata`** — transport identity: source, sequence, timestamp  

### Core — module / edge language

- **`Neuron` / `Neuron<TState>`** — one turn, one commit, sealed Orleans body  
- **`IAnswers<TQuestion,TReply>`** — at most one answerer kind per question  
- **Verbs:** `Emit`, `Reply`, `Ask`, `Schedule`, `Unschedule`  
- **Edge:** `Brain`, `Session` → `EmitAsync`, `SendAsync`, `AskAsync`, `ReadAsync`  
- **Topology facts:** `Connect`, `Disconnect`, `ConnectionRefused`  
- **Outcome facts:** `DeliveryFailed`, `AskExpired`, `ScheduleFailed`  
- **Time facts:** `Schedule`, `Unschedule` (same table as verbs)  
- **Identity / read:** `SynapseRef`; `JournalFact` (+ Cause/Answers/To as **read** fields); `NeuronReading`  
- **Catalog** — listeners, answerers, kind uniqueness, fingerprint  
- **Journal-as-outbox** — watermark dedup, FIFO drain, arm-before-commit, poison-on-commit-fault  
- **Schedule table** — durable pulses; timer + reminder backstop  
- **Connection table** — local emitter routes + ghost rule  
- **Open-ask pins** — horizon → `AskExpired`  
- **Filters + envelope headers** — kind strings only; interface whitelist  
- **Hosting:** `AddDigitalBrain`  

### Explicitly not Stage-1 concepts

- Answer reconstruction / `Answer<,>`  
- `DeclaredRouteSurvives`  
- Fat Abstractions / dual `INeuron` / `Synapse<TReply>`  
- Streams as causal bus; stream adapters (Stage-2 edge)  
- In-neuron `Send` (until Kernel consumer)  
- Stateless workers, placement knobs, hot catalog Revision  
- Multi-owner address space, Kernel behavior ALC, transactions, `IDigitalBrain`  

---

## 6 · Proof obligations after deletion (do not claim green without)

1. Greeter: edge `AskAsync` + journals (Answers stamp; no `Answer<>` type).  
2. Planner → Diary: declaration-only Emit; Cause on **journal read**, thin metadata.  
3. Connect rewires fan-out; bad Connect → `ConnectionRefused`; no `DeclaredRouteSurvives`.  
4. Nested asks: asker `INeuron<TReply>` / `TState` join; restart mid-nest.  
5. DeliveryFailed listenable; Schedule tick after deactivation; AskExpired.  
6. Thrown handler → zero durable trace; commit fault → poison reload.  
7. Root Abstractions public surface = four types (compiler / public API test).  

---

## 7 · Strongest residual risks (honest)

1. **Continuation ceremony** without `Answer<>` — if real modules drown in `TState`, revisit
   Core-only dispatch view (never ABI).  
2. **Schedule pack weight** — correct Stage-1, but largest durable sidecar after outbox; do
   not add cron/calendar algebra.  
3. **Connect ghost rule** — subtle; tests must lock it or authors will invent dual pipelines.  
4. **Pressure to re-fatten Abstractions** for “modules without Core reference” — reject;
   answering *is* Core consumption.  

---

**End of delete pass.** Stage-1 Core is the thin inventory in §5; everything else is Stage-2,
Kernel, or deleted forever.
