# 09 · Contradictions resolved — FINAL LAW

Date: 2026-08-05. Status: **BINDING** over grills 01–08 where they conflict.  
Method: read all ratified decisions in `01`–`08`; extract pairs that cannot both be true;
quote both sides; pick a winner with reasoning; mark the loser **overturned**. After the
table, a single numbered **FINAL LAW** is the non-negotiable Core contract for Stage 1.

**Precedence for this file:** when two grills disagree, this document wins. When grills
agree, this document only restates. Implementation gaps (e.g. depth not yet in
`DeliveryPolicy`) are **law already** — not optional until coded.

Inputs: `01-time-and-reminders.md` … `08-delete-pass.md` only. Scenarios force tests of
expressibility, not second physics.

---

## 0 · What is *not* a contradiction (resolved by reading)

These were flagged as tension spots but **already agree** once vocabulary is split.

| Tension | Why it is not a conflict |
|---|---|
| User “timers/reminders from modules” vs Schedule in Core (01, 05, 08) | User forbids **product** timer UX and **Orleans** reminder APIs on modules — not Core deferred self-delivery. All three grills: Core owns *how* (schedule table + timers + `OutboxWakeup`); modules own *what/when* via `Schedule` facts/verbs; optional **Time module** builds countdown/cron/snooze on that. |
| Multi-owner isolation (04, 06, 07, 08) | Same Stage-1 rule: **one owner per deployment**; no `OwnerId` on `NeuronId`; sc25/27 are product acceptance language, not a Core shared-silo green bar. |
| Scenario prose saying “streams” vs streams edge-only (02, 05, 06) | Prose names UX/ingress patterns. Law: **journals + Deliver** are n2n authority; streams only as edge SSE/telemetry **mirror** or **ingress adapter → journal first** (sc28). |
| Behaviors need targets vs Stage-1 no in-neuron `Send` (02, 04, 08) | Targets = **Connect** rows, **Reply** (turn source), **Ask** answerer route, **edge** `Session.SendAsync`. None of the fifty scenarios force free in-neuron `Send`. |
| Schedule vs outbox wakeup | Different jobs, one companion reminder grain: outbox/ask pins = unsettled speech; schedule = deferred **intent** with empty outbox (sc46). |

---

## 1 · Real contradictions

### C1 · Hop depth: deleted (03) vs MaximumDepth = 16 (07)

**Side A — 03 · Journal durability, C6 (ratified):**

> Core v2 **does not** reintroduce hop-depth as delivery identity. Cycle control is
> structural: … Unbounded graph chatter is bounded by **retry horizon + attempts**, not a
> hop counter … Hop-depth as wire metadata stays deleted.

**Side B — 07 · Failure isolation, §2 (ratified):**

> Doc 03 **deleted** hop-depth … That claim is **half-right and half-fatal** … sc36 …
> need a bound that fires on **successful** hops. Retry physics never see them.
> **03's delete is overturned here** for storm control … Port v1 **MaximumDepth = 16** as
> Core **storm / cycle budget**, not identity … Depth is Core-private … **Not** on
> Abstractions `SynapseMetadata`.

| | |
|---|---|
| **Winner** | **07** — `DeliveryPolicy.MaximumDepth = 16`, Core-private carry on said emissions; depth-exceeded → `DeliveryFailed` attempt **1** terminal on sender. |
| **Loser overturned** | **03 C6** claim that hop-depth is fully absent and that horizons alone bound chatter. |
| **What still stands from 03** | Depth is **not** delivery identity; dedup remains `(Source, Sequence)` only; no `EmitAtDepthAsync`; no public depth on `SynapseMetadata`. |
| **Reasoning** | Successful multi-kind cycles and script Emit storms never burn retries. Structural “no self fan-out” does not stop A→B→C→A. Module-visible depth APIs caused gaming; Core-stamped private depth does not. Horizons stay for **failed** delivery. |

---

### C2 · `DeclaredRouteSurvives`: Core outcome (02) vs delete forever (08)

**Side A — 02 · Communication bus, outcomes table:**

> `DeclaredRouteSurvives` | Emitter on Disconnect | Honest topology after unwire

Also proof obligation 3: Disconnect “journals `DeclaredRouteSurvives` when applicable.”

**Side B — 08 · Delete pass, G-D3 / pack table:**

> **DELETE forever `DeclaredRouteSurvives`.** Disconnect success + connection table +
> catalog already answer “declaration resumes.” No scenario names that fact; it is honesty
> cosplay.

| | |
|---|---|
| **Winner** | **08** — no `DeclaredRouteSurvives` kind. |
| **Loser overturned** | **02** listing and proof obligation that require journaling that fact. |
| **What still stands from 02** | Connect/Disconnect + ghost rule; after Disconnect, declared same-context routes are live again; `ConnectionRefused` on bad Connect. |
| **Reasoning** | Prefer delete. Table + catalog + journaled Connect/Disconnect already reconstruct topology. Extra outcome kind has no scenario consumer and inflates the closed Core pack. |

---

### C3 · Schedule / Core pack home: “Abstractions” wording (01) vs Core-only pack (08)

**Side A — 01 · Time, §5.2:**

> Public Core facts (`DigitalBrain` / **Abstractions**)  
> `Schedule`, `Unschedule`, `ScheduleFailed`

**Side B — 08 · Delete pass, G-D5 / §3.1:**

> Core pack records | **DEL** from Abstractions | **S1** in Core (shrunk)  
> **Ratified Abstractions = 4 types:** `Synapse`, `INeuron<T>`, `NeuronId`, `SynapseMetadata`.

| | |
|---|---|
| **Winner** | **08** — Schedule vocabulary lives in **Core assembly** (with Connect/Disconnect/outcomes). Abstractions stay exactly four types. |
| **Loser overturned** | **01** placement of Schedule facts in Abstractions (and any “fat ABI for physics facts” reading). |
| **What still stands from 01** | Entire hybrid decision D: verbs, period-only API, reserved interception, `ScheduleFailed`, no module `IRemindable`, product Time module on top. |
| **Reasoning** | Thin ABI is load-bearing (02 P3, 04 package boundaries, 08 reset). Modules that schedule already reference Core (`Neuron` base). Putting physics packs in Abstractions freezes protocol as public language and breaks the four-type bar. |

---

### C4 · Cause/Answers on public metadata (03 soft) vs identity-only metadata (08)

**Side A — 03 · What modules read:**

> Metadata (Source, Sequence, Timestamp **[, Cause/Answers if ABI exposes via entry projection]**)

**Side B — 08 · G-D7:**

> Cause / Answers | Durable `JournalEntry` + **fields on `JournalFact`**, not author-stamped
> metadata  
> `SynapseMetadata` (public) | Source, Sequence, Timestamp **only**

| | |
|---|---|
| **Winner** | **08** — public `SynapseMetadata` is identity-only; Cause/Answers appear on **journal read models** / durable entry, never as author API. |
| **Loser overturned** | **03** optional Cause/Answers on public metadata envelope. |
| **What still stands from 03** | Journals are causal truth; modules do not stamp lineage; `Brain.ReadAsync` exposes committed journal lines. |
| **Reasoning** | Ambient envelope / author Cause was defeated as correlation-as-API (02, 07). Soft “if ABI exposes” reopens the door. Read models may show lineage; verbs take bodies only. |

---

### C5 · Answer reconstruction / shape-fingerprint continuation (04) vs Stage-1 delete (08)

**Side A — 04 · Catalog maps:**

> `shapeFingerprints` | per-fact property-name hash (**drift guard before Answer rehydrate**)  
> `continuations` | (neuronKind, question) claims for ask-guard / **Answer reconstruction**

**Side B — 08 · G-D4:**

> **DELETE Stage-1 (and forever from Abstractions)** [Answer reconstruction]. Continuations
> Stage-1: return `TReply?`; later Emit/Reply of `TReply`; asker `INeuron<TReply>` and/or
> `TState`. Catalog drops: `Answer<,>` continuation rows, shape fingerprints for
> reconstruction…

| | |
|---|---|
| **Winner** | **08** — no Stage-1 `Answer<>` reconstruct path; no catalog shape-fingerprint **for reconstruction**. |
| **Loser overturned** | **04** Stage-1 commitment to Answer rehydrate + shape fingerprints as continuation machinery. |
| **What still stands from 02/04** | Core `IAnswers<TQ,TR>`; ≤1 answerer kind per question; open-ask pins; `Ask` ≠ `Emit(question)`; ask without continuation throws in-turn (except edge session journal close). |
| **Reasoning** | Reconstruction is Core cost and a parallel non-journalable type. Nested workflows (sc37) are multi-turn facts + one open ask per kind + join in state. Revisit **only** as Core-only non-journaled dispatch view after measured ceremony tax — never ABI. |

---

### C6 · In-neuron `Send`: deferred (02/08) vs residual “any behavior targets” folklore

**Side A — 02 · Stage boundaries / verb table:**

> In-neuron `Send` | **Deferred** (not Stage 1)  
> Instance routing = Connect rows. Answer routing = Ask/Reply. Edge directed speech = Send.

**Side B — Informal / OS pressure (attack already in 02, not a winning ratification):**

> Module authors need `protected void Send(NeuronId to, Synapse fact)` … behaviors can
> target learned addresses (“any behavior”).

| | |
|---|---|
| **Winner** | **02 + 08** — Stage 1: **no** in-neuron `Send`. Edge `Session.SendAsync` only. Later only with a named Kernel consumer that Connect/Reply cannot cover. |
| **Loser overturned** | Any Stage-1 claim that behaviors require free `Send(NeuronId)` or `Send<TNeuron>`. |
| **Reasoning** | Free Send mints silent parallel worlds under virtual actors; type-coupled Send is orchestration-by-class. Connect is rewire-as-fact; Reply covers non-ask directed response without envelope leak. |

---

### C7 · User correction misread as “no Schedule in Core” vs hybrid (01/05/08)

**Side A — User-shaped kill (grilled in 01 option C / 08 G-D1 attack):**

> timers and reminders might actually come from modules; core might not expose those
> timers and reminders.  
> (Attack form: delete Core schedule table; Time module or raw reminders own delay.)

**Side B — 01 RATIFIED D, 05 §3.4, 08 G-D1 STAND:**

> Modules schedule facts; Core owns waking the neuron into a turn; Orleans
> timers/reminders are never a module API; product "reminders" are a Time module, not Core.  
> Module time = Schedule facts/verbs; Core time = timers+reminders.  
> **STAND — Schedule stays Stage-1 Core.**

| | |
|---|---|
| **Winner** | **01 D / 05 / 08** — Core Schedule table + verbs + reserved facts + `ScheduleFailed`. |
| **Loser overturned** | Outbox-only Core (01 B); Time-as-sole-scheduler (01 C); any reading of the user correction that removes deferred self-delivery from Core. |
| **Reasoning** | Scenarios 31 idle recheck, 39 nightly, 46 30-day dormant, 50 brief, 35 delayed heal **die** without durable deferred facts after deactivation. Hiding wake physics in a privileged Time module relocates complexity and makes every poller a Time client. User correction is naming/surface: no product “reminder” types, no module `IRemindable`. |

---

### C8 · Streams-as-authority in scenario language vs edge-only law

**Side A — Scenario titles/prose (e.g. sc10, sc28, sc47) and residual product talk:**

> “live dashboard stream,” “implicit stream wake,” “pubsub many dashboards.”

**Side B — 02 P2 / streams ratification; 05 §3.5; 06 §5; 03 F13; 08:**

> Streams = **edge / ingress only**, not n2n authority.  
> Stream as sole n2n delivery | **Forbidden**  
> Late-join causal history = **journal read**, never stream replay.

| | |
|---|---|
| **Winner** | **02/05/06/03/08** — one causal bus. |
| **Loser overturned** | Any design that treats Orleans streams (or SSE alone) as neuron↔neuron truth, catalog substitute, or transcript authority. |
| **Scenario mapping (lawful)** | sc47 = Emit + Connect + per-dashboard journals + edge SSE mirror; sc28 = stream → **adapter** → first journaled Deliver; sc10 = same as sc47. |
| **Reasoning** | Late join and dual truth are documented death modes. Poll/journal Stage 1 is correct; stream adapters optional Stage 2 **mirrors**. |

---

### C9 · Multi-owner shared silo scenarios vs Stage-1 deployment isolation

**Side A — Product scenario pressure (sc25, sc27 titles):**

> owner isolation / multi-owner isolation (often read as one silo, many owners).

**Side B — 04 §5, 07 §6, 08 inventory:**

> **One owner per deployment.** Isolation = separate AppHosts / storage / catalogs …  
> Stage-1 claim: multi-owner shared silo is **not** a Core green bar.

| | |
|---|---|
| **Winner** | **04/07/08** — deployment isolation; no `OwnerId` / tenant field on Abstractions. |
| **Loser overturned** | Stage-1 Core implementation of shared-silo multi-tenant keys, RequestContext owner headers as authority, or Name-prefix tenancy (`ada:desk`) as physics. |
| **What product may still claim** | Two deployments never mix journals; same kind strings in different brains; Kernel IdP → deployment routing later. |
| **Reasoning** | Dual identity schemes and forgeable filter headers are silent-success classes. When product forces shared silo, Kernel designs partition once — not a silent fourth field on `NeuronId`. |

---

### C10 · 02 proof obligation “DeclaredRouteSurvives” vs C2 (housekeeping)

Subsumed by **C2**. Any test that *requires* that kind is non-binding; tests that require
Disconnect restoring declared fan-out remain binding.

---

## 2 · Overturned index (quick lookup)

| Overturned claim | Source | Superseded by |
|---|---|---|
| No hop-depth at all; horizons bound successful storms | 03 C6 | 07 depth = 16 private storm budget |
| `DeclaredRouteSurvives` is Core vocabulary | 02 outcomes / proofs | 08 delete forever |
| Schedule / Core pack in Abstractions | 01 §5.2 wording | 08 four-type ABI; pack in Core |
| Cause/Answers optional on public `SynapseMetadata` | 03 read shape soft | 08 identity-only metadata |
| Stage-1 Answer reconstruct + shape fingerprints for it | 04 catalog maps | 08 G-D4 |
| Stage-1 in-neuron `Send` for behaviors | OS folklore / rejected attack | 02/08 edge Send + Connect + Reply |
| No Core Schedule (user correction misread / option B or C sole) | kill attacks in 01/08 | 01 D, 05, 08 STAND |
| Streams as n2n or catalog | scenario slang / bad designs | 02/05/06 edge-only |
| Shared-silo multi-owner as Stage-1 Core | sc25/27 overread | 04/07/08 deployment isolation |

---

## 3 · FINAL LAW

Non-negotiable for Stage-1 Core. Numbered for citation. Changing a law requires a new
grill document that explicitly overturns this file — not a silent patch.

### Bus and turns

1. **One causal bus.** Speech is staged in-turn → one commit (journal said-entry with
   receiver snapshot) → post-commit drain → direct `Deliver`. No second bus.
2. **Commit-before-dispatch.** Return of `Deliver` means the *receiver* committed its
   turn staging path under at-least-once rules — never “the answer arrived in the same
   turn.” Emitting handler never awaits remote `Deliver` or `Drain`.
3. **No neuron-awaits-neuron.** Continuations are later turns. Edge `AskAsync` is journal
   observation sugar, not wire RPC. Same-turn reply ride-back is **forbidden** as a
   correctness path.
4. **Serialized turns.** Default single-threaded activation. `[Reentrant]`,
   `[MayInterleave]`, module `[AlwaysInterleave]`/`[ReadOnly]`, extra grain interfaces,
   `IRemindable`, `[StatelessWorker]` on neurons are **boot-refused**. Self-delivery is a
   direct method call; self-proxy throws.
5. **Surgical read interleave only.** Core `ITransport` committed reads may pair
   `[AlwaysInterleave]` + `[ReadOnly]`. Deliver, drain, session mutators, schedule ticks
   never interleave.

### Speech verbs and topology

6. **Resolution modes, not transports.** Stage-1 verbs: `Emit`, `Reply`, `Ask`,
   `Schedule`, `Unschedule`. Edge: `Session.EmitAsync`, `SendAsync`, `AskAsync`,
   `Brain.ReadAsync`. **No** `Broadcast` API. **No** type-coupled `Send<TNeuron>`. **No**
   in-neuron `Send` until a named Kernel consumer forces a new grill.
7. **Emit** = declaration listeners @ emitter.Name (ghost-suppressed) ∪ connections for
   fact kind; `to: []` legal. **Reply** = turn source (+ ordinary overhear rules);
   Core-private source; stamps `Answers` when closing an open ask. **Ask** = catalog
   answerer @ asker.Name + open-ask pin; not Connect-able; distinct from `Emit(question)`.
8. **Connect / Disconnect stay.** Local durable rows on the **emitter**, journaled via the
   ordinary bus. Ghost rule stands. No emit-path registry. **No** `DeclaredRouteSurvives`
   kind (overturned — table + catalog suffice).
9. **Edge `SendAsync`** pins exact `NeuronId` only — no declaration/connection fan-out.
   Session **is** a neuron (`session` / context name). No edge ghost source.

### Ask protocol

10. **Abstractions stay four types:** `Synapse`, `INeuron<T>`, `NeuronId`,
    `SynapseMetadata` (Source, Sequence, Timestamp only).
11. **Core owns** `IAnswers<TQuestion,TReply>` (or equivalent answerer role), open-ask
    tables, Answers stamps. ≤1 answerer kind per question type (boot fail on 2+).
12. **Stage-1 continuations:** answerer return `TReply?` (null = defer); later
    `Emit`/`Reply` of matching `TReply`; asker `INeuron<TReply>` and/or `TState` join.
    **No** Stage-1 `Answer<>` reconstruction; **no** Abstractions `Synapse<TReply>` /
    dual `INeuron<TQ,TR>`.
13. **Edge AskAsync:** fire ask once on session; observe **session journal** for
    Answers / `DeliveryFailed` / `AskExpired`; wire failure → `AskOutcomeUnknownException`
    → read journal, never refire.

### Time

14. **Core Schedule is Stage-1 physics.** Durable per-neuron schedule table; in-turn
    `Schedule(fact, period)` / `Unschedule<TFact>()`; reserved facts `Schedule` /
    `Unschedule` (Core-intercepted; modules must not `INeuron<>` them); listenable
    `ScheduleFailed` after consecutive tick failures then unschedule.
15. **Orleans timers and reminders are Core-only.** `OutboxWakeup` (`IRemindable`)
    backstops unsettled outbox **or** ask pins **or** schedules. Modules never
    `RegisterGrainTimer` / `RegisterOrUpdateReminder` / implement `IRemindable`.
16. **Product time is a module.** Countdown, cron, TZ, snooze, NL “remind me” sit on Core
    Schedule + ordinary synapses — not in Core vocabulary and not as Orleans APIs.
    Period-only Core API; one-shot and cron-like due math are module idioms.
17. **High-frequency ingress ≠ Schedule.** Firehose → stream/adapter → journaled facts.
    Schedule is cadence and delayed intent.

### Journal durability

18. **Journal-as-outbox.** One journal; said lines are payload; lazy progress; durable
    cursor. No separate payload outbox. No module-minted `IDurable*` keys.
19. **Post-handler staging + one `WriteStateAsync`.** Handler throw → zero durable trace.
    Commit failure → **poison** + deactivate + reload. No retraction machinery.
20. **At-least-once + watermark** on `(Source, Sequence)`; duplicate → silent success ack.
    Never throw on redelivery. Never GUID/window eviction dedup.
21. **FIFO per receiver + abandonment barrier.** Terminal/exhaustion → stage
    `DeliveryFailed` on **sender**, **commit hole before unblocking** receiver.
22. **Arm wakeup before commit** when deliverables / pins / schedules require it.
23. **Horizons and bounds** live in one `DeliveryPolicy` home (attempts, retry horizon,
    attempt timeout, drain interval, wakeup cadence, ask horizon, schedule failure limit,
    compaction floor subordinate soft targets). Infinite silent retry is forbidden.
24. **Compaction floor hard:** never below `min(cursor, oldest ask pin, …)`. Tallies
    outlive eviction.

### Depth (storm control)

25. **MaximumDepth = 16** (Core-private on said emissions / outbox shape). Edge-born and
    pure schedule-born facts start at depth 1; each hop +1. Exceeded →
    `DeliveryFailed(… depth …)` on sender, attempt **1** terminal. **Not** identity; **not**
    on public `SynapseMetadata`; **no** module read/set/reset API. Horizons still bound
    **failed** delivery only.

### Outcomes and heal

26. **Failure is vocabulary.** Core mints (listenable): `DeliveryFailed` (sender/drain
    only), `AskExpired`, `ConnectionRefused`, `ScheduleFailed`. Modules hear and compose
    self-heal; modules do not mint transport-true delivery failures.
27. **Closed Stage-1 Core synapse pack (Core assembly, not Abstractions):**
    `Connect`, `Disconnect`, `ConnectionRefused`, `DeliveryFailed`, `AskExpired`,
    `Schedule`, `Unschedule`, `ScheduleFailed`. No `DeclaredRouteSurvives`, no `Completed`.

### Provenance and isolation

28. **Source is Core-stamped** from the committing activation’s `NeuronId`. Modules pass
    bodies only. No author Cause/Answers/Source/Sequence stamping. RequestContext is
    transport convenience only; journal is authority on redelivery.
29. **Synapse body is data.** Prompt injection, trust tags, egress gates, share redaction
    are modules/Kernel. Core does not evaluate body text as privileged instruction.
30. **Stage-1 multi-owner = one owner per deployment.** No `OwnerId`/`TenantId`/`RevisionId`
    on `NeuronId`. Within-brain isolation = `NeuronId(Kind, Name)` locus + per-neuron
    journals. Shared-silo multi-tenant is not a Stage-1 Core green bar.
31. **No Core journal-share API.** Guest panes get derived projection facts (modules/edge),
    never raw owner journal dump.

### Orleans seal and packages

32. **Orleans is Core’s body; modules never import Orleans** in public contracts. Filters
    whitelist neuron wire: transport, drain, session, Orleans runtime namespaces only.
33. **Streams: edge/ingress adapters only.** Never authoritative n2n delivery; never
    catalog substitute; never causal history authority. Stage 1 may ship zero stream code;
    policy still forbids second-bus inventions.
34. **Catalog** = one pure `Build` from type set; fingerprint = declaration-row SHA-256;
    kind collisions and reserved hijack fail boot. No dual derivation; no emit-path
    registry; Stage-1 catalog change = redeploy (hot Revision = Stage 3 epoch hook only).
35. **Behavior = neuron.** Kernel owns author/compile/gate/lifecycle; Core owns dispatch
    only. No script RPC, no Roslyn in Core, no `IModule`, no `IDigitalBrain` mega-facade.
36. **Module state = `TState` slot** committed in the turn batch. External stores are IO,
    not second Orleans journal keys. Escape hatches (`WriteStateAsync`, `GrainFactory`,
    `DeactivateOnIdle`) sealed obsolete/error.
37. **UI is module synapses** read as `JournalFact` bodies. No Core widgets / RichMessage
    ABI. Progressive UI = observe journals (and optional Stage-2 watch with same cursor);
    token SSE is projection, not substitute for journaled terminal facts.
38. **Core public edge is exactly** `Brain` + `Session` (speech + `ReadAsync`). Product may
    wrap outside Core; Core does not ratify a fat facade.
39. **Transactions: forbidden.** Stateless workers: never on `Neuron`; optional later
    non-neuron offload behind DI. Placement attributes: Core/host only, never modules.
40. **Prefer delete.** One mechanism per job. A Stage-1 type/verb with no consumer is not
    Stage-1. Prefer structural bans over policy commentary.

---

## 4 · Scenario language → law (cheat sheet)

| Scenario slang | Lawful mechanism |
|---|---|
| “Stream the dashboard” | Emit KPI facts → dashboard neurons journal → edge `ReadAsync` / SSE **mirror** |
| “Implicit stream wake” | External stream → **adapter grain** → journaled Deliver (sc28) |
| “Reminder in 30 days” | `Schedule(dueFact, delay)` + Core wakeup; product chrome = Time module |
| “Behavior targets instance” | `Connect` then Emit; or Reply; or edge Send — not in-neuron Send |
| “Multi-owner isolation” | Two deployments / storage partitions (Stage 1) |
| “Share the pane” | Derived guest facts; never `ReadAsync` of owner journals as product default |
| “Infinite script loop” | Depth 16 + terminal `DeliveryFailed`; module must still gate external effects |

---

## 5 · Implement-before-claim gaps (law already; code may lag)

These are **not** open design questions. They are owed green tests:

1. `DeliveryPolicy.MaximumDepth == 16` + B1–B5 chain tests (07 checklist).
2. `Reply` verb on `Neuron` if still missing (02, 08).
3. Abstractions public surface = four types; Core pack relocated; no
   `DeclaredRouteSurvives`.
4. No Stage-1 Answer reconstruction path.
5. Schedule + idle 30-day wake (sc46 shape) under sealed reminders.
6. Root gate: no streams-as-sole n2n path in tests.

---

## 6 · Ratification stamp

**BINDING 2026-08-05.**

- Contradictions **C1–C9** resolved as above; overturned claims must not re-enter design
  prose or code review as “still ratified.”
- **FINAL LAW §3 (1–40)** is the non-negotiable Stage-1 contract.
- Grills 01–08 remain historical argument; where they conflict with this file, **this file
  wins**.

*Prefer delete. One bus. Schedule facts, not Orleans. Depth kills storms, not identity.
Deployment isolates owners. Streams never own truth.*
