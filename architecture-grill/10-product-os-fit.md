# 10 · Product / OS fit — is Stage-1 Core a product-ready OS kernel?

Date: 2026-08-05. Role: product architect. Status: **GRILLED** (recommendation locked).  
Method: brainstorm → strongest attack → defend or fold → score.  
Inputs: `CORE-ARCHITECTURE.md` (ratified), `OS.md`, `CONTEXT.md`, grills **01–08**,
`scenarios/README.md` (50 stress cases). Does **not** re-litigate turn physics, bus algebra,
or Orleans seal — those are assumed.

**Question under grill:** Is Stage-1 Core **product-ready as an OS kernel**, or is a
load-bearing concept still missing?

**OS claim under stress:** behaviors expressible as **communication, not orchestration** —
"any behavior expressible" as a composition of the algebra, not as arbitrary concurrent
programming.

---

## 0 · Verdict (one page)

| Claim | Verdict |
|---|---|
| Stage-1 Core is a **complete nervous-system kernel** for one owner, one catalog epoch, fact protocols | **Yes — design-complete.** Physics cover the algebra. |
| Stage-1 Core is a **shippable product OS** (install behaviors live, multi-device shell, marketplace) | **No.** That is **Kernel + modules + edge host**. CONTEXT deliberately separates Core from OS/product host. |
| A **load-bearing Core concept** is still missing for the 50 scenarios' *choreography* | **No new Core primitive.** Residual gaps are **stage fences**, **implement-before-claim**, or **product modules**. |
| "Any behavior expressible" is true without qualification | **False if read as "any concurrent style / any product UX."** True if read as: **any durable owner capability expressible as neurons + synapses + the Stage-1 verb algebra**, with intentional bans for death modes. |

**Recommendation:** Ship Stage-1 Core as **kernel physics**, not as "DigitalBrain OS v1."
Do not grow Core to close product gaps (hot install, IdP, UI schema, cron DSL). Close
**implement gaps** (thin Abstractions reset, `Reply`, depth budget) before claiming Core
green. Kernel is the product OS layer that *consumes* Core.

**One-line rule:** Core is the nervous system; Kernel is the OS; modules are the body.

---

## 1 · OS metaphor mapping

Traditional OS vocabulary mapped onto DigitalBrain. The point is **load-bearing analogy**,
not cosplay — where the map breaks, the product claim must shrink or Kernel must own it.

### 1.1 Core mapping (Stage-1)

| Classical OS | DigitalBrain | Notes |
|---|---|---|
| **Process** (pid, address space, one thread of execution for the model) | **Neuron** `(Kind, Name)` | Stable identity; serialized turns = single-threaded process model. Name = **locus** (conversation / instance context), not "argv." |
| **Thread / syscall trap** | **Turn** | Atomic unit of existence. Handler runs; one commit; nothing observable outside a turn. |
| **Syscall** | **Verb** (`Emit`, `Ask`, `Reply`, `Schedule`, `Unschedule`; edge also `Send`) | Closed instruction set. Modules cannot invent wire paths; they only stage speech. |
| **Message / IPC payload** | **Synapse** (immutable fact body) | Content is data, never code. |
| **Envelope / credentials on the wire** | **`SynapseMetadata`** (Source, Sequence, Timestamp) | Identity only. Causation is **journal structure**, not author-stamped envelope fields. |
| **Address (pid / socket)** | **`NeuronId(Kind, Name)`** | String kinds forever — journals outlive assemblies. Never `System.Type` / AQN. |
| **Listening socket / service registration** | **`INeuron<T>` declaration** | Hearing **is** subscription and address surface. "Nobody wires anything" for ambient fan-out. |
| **Filesystem mount / device driver table** | **Catalog** | Boot reflection: who hears, who answers, kind uniqueness, fingerprint. Topology capability. |
| **Named pipe / dynamic route table** | **`Connect` / `Disconnect`** on **emitter** | Instance wiring without redeploy. Ghost rule = no dual pipeline at same name for that kind. |
| **File / inode + write-ahead log** | **Journal entry** (heard/said) | Sole causal truth. Journal **is** the outbox payload. |
| **File read API** | **`Brain.ReadAsync` → `JournalFact` / `NeuronReading`** | Committed slices only; UI/audit/Ask completion. |
| **Scheduler (ready queue)** | **Outbox drain + schedule ticks** | Timers/reminders are Core body; modules schedule **facts**, not Orleans reminders. |
| **Signal (SIGTERM-class outcomes)** | **Outcome synapses** (`DeliveryFailed`, `AskExpired`, `ScheduleFailed`, `ConnectionRefused`) | Loud, listenable, composition-friendly. Never silent loss. |
| **Userspace program** | **Module** (compiled sealed synapses + `Neuron` kinds) | Ships vocabulary + handlers; never Orleans in public contracts. |
| **Shell / init client** | **`Brain` + `Session`** | Session **is** a neuron (`session/{context}`). Edge speech + journal observation. |
| **`fork` / dynamic loader** | **Catalog epoch / Revision** | Stage-1 = redeploy composition. Hot load is Kernel Stage 3 — not missing Core physics, deferred product. |
| **Capability / LSM / seccomp** | **Kernel capability gate + filters** | Core Stage-1: provenance + second-wire refusal. Policy is modules (sc43). |
| **Device driver / NIC** | **Ingress adapter / UiEdge** | Streams = edge only; first act is journal. |
| **Shared memory** | **Does not exist** | Join = `TState` + later facts + journal read. No dual truth. |
| **RPC / `await otherProcess`** | **Forbidden** | Continuations are later turns. Edge `AskAsync` observes **session journal**. |

### 1.2 Where the metaphor is intentional (not incomplete)

| Classical feature | DigitalBrain stance | Why that is product-correct |
|---|---|---|
| Multi-tenant kernel | One **owner per deployment** | Isolation = separate brains, not OwnerId in every address. |
| Kernel UI toolkit | UI = **module synapses** | Core does not freeze widgets. |
| Cron daemon in kernel | **Schedule period + Time module** | Cron/TZ/snooze are unbounded product surface. |
| Distributed transactions | **Fact sagas** | Email/model calls do not two-phase-commit. |
| Global `/proc` dump of all processes | **No unscoped journal dump** | Per-neuron read by address; share is projection (sc42). |
| In-process plugin `dlopen` as Stage-1 | **Fingerprint redeploy** | Fake hot registry recreates dual derivation. |

### 1.3 Kernel vs Core (CONTEXT language — do not conflate)

| Term | Job |
|---|---|
| **Core** | Programming paradigm + invariant runtime physics. *Avoid calling it "the OS."* |
| **Kernel** | Deployable OS **on** Core: behavior authoring/lifecycle, capability composition, Revision, owner tap. |
| **Product** | Kernel + modules + edge hosts + Flutter shell — what the owner *uses*. |

Stage-1 Core readiness ≠ product OS readiness. Grilling them as one thing recreates kitchen-sink
Core.

---

## 2 · What Core is / is not

### 2.1 Core **is** (load-bearing product promise)

1. **Thin ABI** — exactly four Abstractions types: `Synapse`, `INeuron<T>`, `NeuronId`,
   `SynapseMetadata` (identity-only).
2. **One causal bus** — commit said-entry with receiver snapshot → drain → direct `Deliver`;
   at-least-once + watermark; journal-as-outbox.
3. **Turn physics** — post-handler staging, one batch commit, poison on ambiguous write,
   zero durable trace on handler throw.
4. **Speech algebra** — Emit / Ask / Reply / Schedule·Unschedule; edge Send; no second
   `Broadcast` API; streams never n2n authority.
5. **Topology** — declaration capability ∪ connection instance wiring + ghost rule; local
   emitter tables only.
6. **Ask protocol** — Core `IAnswers<,>`, open asks, Answers stamp on journal, edge
   `AskAsync` = fire once + journal observe.
7. **Time physics** — durable schedule table; modules schedule facts; Orleans timers/reminders
   sealed inside Core.
8. **Failure as vocabulary** — terminal outcomes listenable for self-heal composition.
9. **Orleans as body, sealed from modules** — full platform power without module-visible grain
   knobs.
10. **Edge thinness** — `Brain` + `Session` only; session is a neuron; UI is module facts.
11. **Self-awareness substrate** — journals + enumerable catalog topology; "why" is reconstructable
    without synthetic observations.
12. **Stage-1 growth seam** — catalog fingerprint (epoch hook later); Connect rewires without code
    change in speakers.

### 2.2 Core **is not**

| Temptation | Owner |
|---|---|
| Behavior Studio, Roslyn, collectible ALC, marketplace | Kernel |
| Gmail / Salesforce / crypto / chat product vocabulary | Modules |
| Flutter / HTTP / SSE product surface | UiEdge / shell host |
| Multi-owner shared silo, IdP | Deployment + Kernel later |
| Cron / TZ / countdown chrome | Time module on Schedule |
| Prompt-injection policy engine | Policy modules (Core: Source unforgeable, body = data) |
| Share / redaction / legal hold completeness | Modules + Kernel break-glass |
| Workflow engine / saga framework package | Emergent from facts |
| `IDigitalBrain` mega-facade | Forbidden in Core; product wrapper outside optional |
| Hot catalog without epoch | Stage-3 Kernel+Core |
| In-neuron free `Send` as Stage-1 default | Deferred until Kernel consumer forces |

### 2.3 Product completeness vs Core completeness

```
Product OS completeness
  = Core physics (this grill: ~design done)
  + Kernel (behavior lifecycle, capability, Revision)
  + Module pack (ingress, AI, shell, time, tasks, …)
  + Edge hosts (UiEdge, MCP, Flutter)
  + Proof (live journal-driven scenarios, not only unit doubles)
```

A green Core suite proves **wiring and physics**. It does not prove a morning-brief product
day. That split is already product doctrine (live journal oracles).

---

## 3 · Grill: "product-ready as OS kernel?"

### Attack A — Core is not an OS until behaviors hot-install

**Attack:** Scenarios 05 / 19 / 36 / 49 are the product differentiator. Without live install,
Core is a distributed actor framework with pretty names.

**Defense:** Those scenarios force **behavior = neuron** and **declaration fan-out N+1**, not
Roslyn-in-Core. Stage-1 proves N+1 by **redeploy composition** (speakers unchanged when a new
listener kind appears). Hot Revision is Kernel Stage 3 with Core epoch **hook only**. Faking
hot-load without epochs is dual derivation — a known death mode.

**Decision:** **Not a missing Core concept.** Missing **Kernel product**. Stage-1 Core is
kernel-ready for behavior *execution*; not for behavior *studio*.

---

### Attack B — Without in-neuron `Send`, "any behavior" is a lie

**Attack:** OS.md draws three routings (Emit / Reply / Send). Stage-1 deletes in-neuron Send.
A behavior that learns an address cannot speak to it.

**Defense (from grill 02 / G7):** Across fifty scenarios, directed instance work is
**Connect rows**, **Reply-to-source**, or **edge Session.Send**. Free in-neuron Send is the
soft form of orchestration-by-address-book and mints silent parallel virtual-actor worlds on
typos. Add only when a named Kernel behavior-creator proves Connect/Reply/edge-Send cannot
cover a one-shot directed fact.

**Decision:** **Intentional Stage-1 fence, not a hole in the algebra.** Document in product
language: *behaviors rewire with Connect; they do not hold free address books.*

---

### Attack C — Nested cognition needs call stacks

**Attack:** sc37 nested asks (chat → memory → vector) need `await` ergonomics; TState join is
ceremony tax; without `Answer<>` reconstruction Core is not agent-ready.

**Defense:** Nested *workflows* yes; nested *grain awaits* no (reentrancy deadlock class).
Stage-1: open ask + later reply + `INeuron<TReply>` / TState. Ceremony risk is **open**
(architecture risk #1) but not fatal for expressiveness — every nested scenario remains
expressible.

**Decision:** **Expressible.** Ergonomics may force a **Core-only** (never ABI) dispatch view
later — measure, don't fatten now.

---

### Attack D — Multi-owner / share / compliance prove Core isolation is fake

**Attack:** sc25/27/42/14 require tenancy and share; deployment isolation is an ops dodge.

**Defense:** CONTEXT + G23: one owner per brain. Shared-silo multi-tenant without Kernel
identity design dual-keys every journal and stream. Share pane is **derived projection
facts**, not journal export APIs — Core deliberately has no share primitive so journals
never become the default collab bus.

**Decision:** **Product completeness gap, not Core physics gap.** Stage-1 Core isolation
model is coherent and shippable for single-owner product.

---

### Attack E — Depth / storm control / implement gaps mean design is not done

**Attack:** Grill 07 overturns hop-depth delete; Reply may be missing on disk; Abstractions
still fat vs ratified. Design docs ≠ product-ready kernel.

**Defense:** Those are **implement-before-claim** items, not missing *concepts*. The concepts
are ratified (depth as Core-private storm budget; Reply as resolution mode; four-type ABI).
A kernel is product-ready as *design* when the inventory is closed; *ship*-ready when the
checklist is green.

**Decision:** **Design-complete; implementation debt remains.** Do not invent new primitives
while closing debt.

---

### Attack F — Prefer delete: Schedule, Connect, Ask are three systems too many

**Attack:** Delete-pass already thinned hard; still looks thick for "thin kernel."

**Defense (01–04, 08):** Each has a distinct job and a failed delete attempt:

| Mechanism | Job if deleted |
|---|---|
| Schedule | Dormant 30-day intent dies; outbox only wakes unsettled speech |
| Connect | Ghost pipelines; no runtime rewire; behaviors stuck at redeploy |
| Ask vs Emit | Overhearers break answerer cardinality; edge inference dies |

**Decision:** **Stand.** Thickness is closed vocabulary, not open framework surface.

---

## 4 · "Any behavior expressible" — stress test

### 4.1 What the claim means (product architect reading)

| Reading | Valid? |
|---|---|
| Any **owner capability** as durable participants that hear/speak facts, survive restart, and reconstruct "why" | **Yes — target claim** |
| Any **C# concurrent programming style** (await peers, reentrant handlers, shared memory, transactions) | **No — correctly forbidden** |
| Any **product feature** without a module or Kernel | **No — Core is not the product** |
| Any **topology** without redeploy (hot N+1 install) | **Stage-3**, not Stage-1 Core alone |

### 4.2 Expressible with Stage-1 algebra (forced by scenarios)

| Behavior class | Algebra | Scenario anchors |
|---|---|---|
| Ambient pipelines / enrichment | Emit chain + journals | 01, 08, 18, 21 |
| Social → multi-pane dashboard | Emit + Connect + UI facts | 02, 10, 47 |
| Recall / "why" | Read journals + Cause structure | 03, 04, 09, 34, 48 |
| Multi-tool + approval | Ask + open asks + domain gate facts | 07, 15 |
| Nested cognition | Multi-turn Ask + TState / hear reply | 37 |
| Progressive long work | Multi-turn + intermediate UI facts | 29, 30 |
| Cancel / replan | Domain facts + Unschedule; no un-say | 30 |
| Dormant wake / nightly | Schedule + reminder backstop | 39, 46, 50 |
| Self-heal | Hear `DeliveryFailed` / `ScheduleFailed` | 35 |
| Behavior as neuron (compiled) | Same dispatch as modules + Connect | 05, 36 (execute path) |
| Rich multimodal | Module UI synapses | 06, 33, 38 |
| Ingress wake | Adapter → journal first | 28 |
| Owner isolation (deployment) | Separate brains | 25, 27 (as deploy model) |

### 4.3 Not expressible (or not *cleanly*) — and whether that is OK

| Gap | What you cannot do | OK? | Why / who owns |
|---|---|---|---|
| **Neuron-awaits-neuron** | Sync RPC between neurons | **Yes OK** | Death mode; multi-turn facts replace it |
| **Same-turn reply as truth** | HTTP-style ride-back correctness | **Yes OK** | Breaks outbox FIFO; edge journals |
| **In-neuron free Send (Stage-1)** | Ad-hoc directed speak to learned id | **OK Stage-1** | Connect / Reply / edge Send; re-open with Kernel consumer |
| **Hot behavior install** | Marketplace → live kind without redeploy | **OK deferred** | Kernel Revision + epoch; not Core ALC |
| **Un-say committed fact** | Distributed undo | **Yes OK** | Compensating facts; committed is causal |
| **Parallel mutators on one neuron** | Multi-threaded one journal | **Yes OK** | Parallelism = more neurons / post-commit fan-out |
| **Multi-owner one silo** | Tenant keys in NeuronId | **OK Stage-1** | Deployment isolation; Kernel later if proven |
| **Global timeline query** | "All facts everywhere" bus | **Yes OK** | Dual truth / unbounded; read addressed journals |
| **Cron-in-Core** | Product calendar DSL in kernel | **Yes OK** | Time module on Schedule |
| **Core UI schema** | One RichMessage for all clients | **Yes OK** | Module pack; Core freezes wrong seam |
| **ACID multi-grain booking** | Orleans Transactions default | **Yes OK** | Saga-by-facts; email doesn't abort |
| **Script `CallAsync` ergonomics** | Behaviors as RPC clients | **Yes OK** | Behaviors *are* neurons |
| **Share raw journals** | Guest ReadAsync of owner life | **Yes OK** | sc42: projection facts only |
| **Injection-proof model choice** | Core taint / sanitize email | **Yes OK** | Policy modules; Core provenance only |
| **Answer<> zero-ceremony join** | Reconstruct Q+R without TState | **Acceptable risk** | Expressible with TState; revisit Core-only view if BDD taxes |
| **Depth > 16 hop storms** | Infinite successful reaction cycles | **Yes OK** | Storm budget; raise constant with proof |
| **Stateless worker as Neuron** | Scale pure stages without journal | **Yes OK** | Non-neuron offload beside; identity stays |

### 4.4 Residual "almost expressible" product frictions (not missing primitives)

1. **Continuation ceremony** — multi-join modules accumulate TState fields. Risk #1 in
   architecture; measure before growing Core.
2. **Ask routing by context name** — wrong locus mints empty parallel worlds. Discipline +
   tests, not Core magic.
3. **Single open-ask per question kind** — sharp under tool storms; multi-ask keys only with
   consumer.
4. **Connect-before-ingest** — ghost routes live until wired; product must Connect early.
5. **Scenario prose still says "streams as n2n" / "Answer<>"** in places — choreography intent
   is Emit/Connect/Ask; docs must not reintroduce dual bus in implementation.

### 4.5 Fold test on the OS claim

**Strongest form of the claim that survives:**

> Any durable behavior an owner wants the brain to perform can be expressed as one or more
> **neurons** that **hear and speak sealed synapses**, using **Emit / Ask / Reply / Connect /
> Schedule** (and edge Send), with **journals** as memory and audit — without orchestrators,
> without awaiting peers, and without a second bus.

**What we refuse to claim:**

> Any program that would be natural on a reentrant thread pool, any multi-tenant SaaS kernel,
> or any hot-plugin marketplace is Stage-1 Core.

That refusal is **product strength**: predecessors died opening those doors.

---

## 5 · Product completeness score

Scores are **product-architect judgment** against the OS claim and fifty scenarios — not a
test runner. Scale: 0–10.

### 5.1 Dimension scores

| Dimension | Score | Rationale |
|---|---|---|
| **Causal bus / delivery physics** | **9** | One bus, journal-outbox, watermark, FIFO, poison, outcomes-as-facts. Implement depth + checklist still owed. |
| **Speech algebra (verbs)** | **8.5** | Emit/Ask/Reply/Schedule + edge Send closed. Reply implement gap; in-neuron Send deferred with eyes open. |
| **Topology (declaration ∪ Connect)** | **9** | Ghost rule load-bearing; N+1 listener install without speaker edits. Hot epoch deferred honestly. |
| **Time / dormant wake** | **9** | Schedule physics + sealed Orleans; product Time module not required for pollers. |
| **Ask / multi-turn cognition** | **8** | Nested expressible; continuation ceremony residual. |
| **Failure / isolation / provenance** | **9** | Structural reentrancy absence; Source unforgeable; deployment multi-owner stance coherent. |
| **Edge / UI substrate** | **8.5** | Session-as-neuron + journal observe is correct; progressive UI is observation not ride-back. |
| **Orleans seal / module safety** | **9** | Defense in depth; power without module knobs. |
| **Behavior / growth as OS** | **5** | Runtime path ready (behavior = neuron); **lifecycle OS (Kernel) absent** from Stage-1 Core by design. |
| **Product surface completeness** (modules, shell, marketplace, multi-device polish) | **2** | Out of Core scope; v1/product packs live elsewhere. |
| **Implement fidelity to ratified design** | **6** | Tree still fatter than delete-pass (fat Abstractions, Answer reconstruction, possible missing Reply/depth). |

### 5.2 Aggregate scores

| Aggregate | Score | Reading |
|---|---|---|
| **Stage-1 Core as nervous-system kernel (design)** | **8.5 / 10** | Ready to build Kernel + modules on; no missing load-bearing Core concept. |
| **Stage-1 Core as shippable kernel (code + proof)** | **6.5 / 10** | Close debt + green checklist (esp. 03/07/08 proof obligations) before the claim. |
| **DigitalBrain as product OS for "any behavior"** | **4 / 10** | Core alone is not the OS. Kernel + modules + live proofs dominate remaining work. |
| **Expressiveness of the algebra vs claim** | **8 / 10** | Claim holds under the precise reading in §4.5; intentional non-goals are correct. |

### 5.3 Score movement conditions

| Event | Score impact |
|---|---|
| Abstractions reset to 4 + Core IAnswers; greeter/planner green | Ship-kernel **6.5 → ~8** |
| Depth budget + DeliveryFailed/Reply proof suite green | Physics **9 → 9.5** |
| Kernel Revision epoch (real, not fake registry) | Behavior/growth **5 → 8**; product OS **4 → 6** |
| First-party module pack + live sc02/sc50 journal oracle | Product OS **4 → 7** |
| Re-adding streams-as-n2n or fat IDigitalBrain | All scores collapse (regression to death modes) |

---

## 6 · Remaining gaps (ordered, owned)

### 6.1 Load-bearing Core concepts — **none missing**

After grills 01–08 + architecture second pass: no scenario choreography requires a **new**
Core primitive beyond the ratified inventory (08 §5). Prefer delete pressure is already
applied.

### 6.2 Stage fences (expressible later, not Stage-1 Core)

| Gap | Stage | Owner |
|---|---|---|
| In-neuron `Send` | 2+ if forced | Core, consumer-gated |
| Journal `WatchAsync` / push observers | 2 | Core edge sugar |
| Stream ingress/egress adapters | 2 | Core edge |
| Stateless worker offload host | 2 | Core beside neurons |
| Placement directors | 2 | Core hosting |
| Catalog epoch / hot Revision | 3 | Kernel + Core hook |
| Capability reification / owner tap | 2–3 | Kernel |
| Multi-ask keys | later | Core if chat storms force |
| Core-only `Answer` dispatch view | later | Core if ceremony measured |

### 6.3 Implement-before-claim (design done, code/tests may lag)

From grills 07–08 and architecture §10:

1. Reset Abstractions to four types; relocate Core pack / read models / `SynapseRef`.
2. Core `IAnswers<,>` + thin public metadata; Cause/Answers on journal read models.
3. `Reply` verb if missing on disk.
4. Depth budget 16 (storm control) + B1–B5 tests.
5. Delete Stage-1 `Answer<>` reconstruction / `DeclaredRouteSurvives` if still present.
6. Proof obligations: greeter, Connect ghost rule, DeliveryFailed heal, Schedule after idle,
   AskExpired, poison, zero-trace throw, nested ask join without peer await.

### 6.4 Product / Kernel gaps (do not solve inside Core)

| Gap | Why not Core |
|---|---|
| Behavior Studio + ALC gate + marketplace | Kitchen sink; Kernel |
| First-party modules (Gmail, AI, shell, Time UX) | Vocabulary packs |
| UiEdge / Flutter multi-device polish | Host |
| OAuth vaults, legal hold product, share tokens | Modules + Kernel |
| Prompt-injection product policy | Modules (Core = provenance) |
| Multi-owner shared economics | Kernel identity design first |

---

## 7 · Recommendations (owner-facing)

1. **Treat Stage-1 Core as kernel physics, not product OS.** Ship language: "Core runs the
   nervous system; Kernel is the OS; modules are life."
2. **Do not open Core** for hot install, OwnerId, UI envelopes, cron DSL, or free in-neuron
   Send without a named consumer and a new grill.
3. **Close implement debt** (08 inventory + 07 checklist) before any "Core is green" claim.
4. **Next product investment after Core vertical slice:** Kernel behavior path *or* a thin
   first-party module pack that forces live journal oracles (sc02 / sc35 / sc46 / sc50) —
   not more Core abstraction.
5. **Hold the OS claim in the precise form (§4.5).** Marketing "any behavior" without the
   multi-turn / no-await reading will force fatal APIs back in.

### Strongest objection to this recommendation

"Investors and users buy an OS, not a grain framework. If Stage-1 cannot live-install a
behavior, the product is not real."

### Defense

Live-install is real product value — it belongs to **Kernel on Core**, not Core itself.
Core's job is that when the behavior *exists*, it is **indistinguishable from a module**:
same journal, same verbs, same heal, same rewire. That is the hard OS property. Studio
without that physics is a chatbot with a compiler. Physics without Studio is still a kernel
you can ship modules on **today** via redeploy composition.

**Fold condition:** only if a single live consumer cannot ship even with redeploy *and*
cannot wait for Kernel — then implement **immutable catalog epoch pointer only**, still
without Roslyn in Core (grill 04 M-fold).

---

## 8 · RATIFIED stance (this grill)

```
DECISION
  Stage-1 Core is design-complete as a single-owner nervous-system kernel.
  It is not product-complete as DigitalBrain OS.
  No additional load-bearing Core concept is required for the 50-scenario algebra.
  "Any behavior expressible" holds for fact-protocol neurons; fails correctly for
  peer-await RPC, dual buses, hot-fake registries, and Core-owned product policy.

OS MAP
  process → neuron · file/log → journal · syscall → verb · message → synapse
  fd/address → NeuronId · listen/register → INeuron<T> · route table → Connect
  scheduler → Schedule + outbox drain · signals → outcome facts
  shell → Brain/Session · userspace → modules · OS distro → Kernel + modules

SCORES (approx)
  Core design kernel fitness ........ 8.5/10
  Core ship readiness ............... 6.5/10
  Product OS completeness ........... 4/10
  Algebra vs precise OS claim ....... 8/10

NEXT
  Implement ratified thin inventory; prove checklists; build Kernel/modules
  outside Core. Prefer delete. Do not grow Core to look more like a product.
```

---

## 9 · Traceability

| Claim | Source |
|---|---|
| Thin ABI, one bus, stage map | `CORE-ARCHITECTURE.md` |
| OS sentences + refuses | `OS.md` |
| Core ≠ OS; Kernel = OS on Core | `CONTEXT.md` |
| Time / Schedule | grill `01` |
| Verbs / Send fence / Connect | grill `02` |
| Journal / poison / outbox | grill `03` |
| Modules / behaviors / multi-owner | grill `04` |
| Orleans seal | grill `05` |
| Edge / Session / UI facts | grill `06` |
| Failure / depth / provenance | grill `07` |
| Minimal Stage-1 inventory | grill `08` |
| Scenario force set | `scenarios/README.md` |

---

*Prefer delete. If the product feels incomplete, add Kernel or a module — not a fifth
Abstraction or a second bus. Core is ready to be stood on; it is not ready to be sold as
the whole OS.*
