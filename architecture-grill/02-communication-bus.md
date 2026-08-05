# 02 · Communication bus — Emit, Send, Reply, Ask, broadcast, streams, subscriptions

Date: 2026-08-05. Method: brainstorm → adversarial self-grill → delete-first → ratify.
Status: **RATIFIED**.

Inputs: baseline physics (owner brief), `CORE-ARCHITECTURE.md`, `CORE-DESIGN.md`,
`CORE-RESEARCH.md`, `OS.md`, `FLOWS.md`, scenarios 02/10/28/35/47, live Core surface
(`Neuron` verbs, `StageSaid`, `Session`, `CoreSynapses`, catalog).

**Non-goals of this grill:** package layout, Kernel behavior ALC, multi-owner IdP,
Orleans Transactions, catalog Revision epochs. Those are other files.

---

## 0 · Physics that this document does not re-litigate

| # | Rule | Communication consequence |
|---|---|---|
| P1 | One causal bus: **journaled commit, then Deliver** | No path that delivers a fact to another neuron before the emitter's said-entry commits. Return of `Deliver` means *committed*, never *the answer*. |
| P2 | Streams = **edge / ingress only**, not n2n authority | A stream may wake an adapter or push UI; it never replaces journaled outbox delivery between neurons. |
| P3 | Thin Abstractions (**four types**) | Speech verbs and ask typing are Core (or edge). They do not fatten the ABI. |
| P4 | **No neuron-awaits-neuron** | Continuations are later turns. Edge `AskAsync` is journal observation sugar, not wire RPC. |
| P5 | **Catalog declarations + Connect** define topology | Two local sources, one resolution snapshot at commit. No remote registry on the emit path. |

If a proposal violates P1–P5, it is rejected even when ergonomic.

---

## 1 · What "the bus" actually is

```
handler stages emissions (in memory)
  → ONE commit (journal said-entries with receiver snapshot + state + tables)
  → post-commit drain (timer / reminder turn)
  → Deliver(fact, envelope) per receiver (direct grain call, at-least-once)
  → receiver watermark on (Source, Sequence) → handle → commit
```

There is **no second bus**. There is **no** product `Broadcast` method. There is **no**
n2n stream topic. "Broadcast" in prose means *address resolution under declarations ∪
connections*. "Directed" means *resolution pinned to one `NeuronId` (or the turn source)*.

```
receivers(said fact) =
    declaredListeners(exact type) @ emitter.Name
      EXCEPT kinds present as connection targets for that factKind   // ghost rule
  ∪ connections[factKind]
  ∪ ask-role targets (answerer | asker) when the emission is an ask / answer
  — dedup by NeuronId; each receiver stamped via: declared | connected | ask
```

Self-delivery never uses the grain proxy (deadlock class, proven). Self is either skipped
on fan-out or entered by direct method call.

---

## 2 · Verb inventory under stress (brainstorm)

Candidates that appear across OS.md, research, design, and code:

| Candidate | Claimed job | Status before this grill |
|---|---|---|
| `Emit(fact)` | Default nervous system speech | Uncontested |
| `Ask(question)` | Speech + answerer route + open-ask pin | Uncontested as distinct from Emit |
| `Reply(fact)` | Directed answer to turn source | OS/research: yes; design deleted; architecture restored |
| `Send(to, fact)` | Directed to learned `NeuronId` | Edge has it; in-neuron deleted in design; architecture Stage-1 edge-only |
| `Broadcast(fact)` | Second name for Emit | Rejected by architecture as dual fire path |
| `Session.AskAsync` | Edge wait-for-reply | Uncontested as journal sugar |
| `Session.EmitAsync` / `SendAsync` | Edge speech | Uncontested |
| Streams (implicit/explicit) | Scale / wake / UI | Contested only if mistaken for n2n bus |
| Declaration-as-subscription | Compile-time hearers | Uncontested |
| `Connect` / `Disconnect` | Runtime instance wiring | Contested by "delete Connect" attack |
| `DeliveryFailed` / `AskExpired` | Terminal outcomes as facts | Uncontested physics; self-heal consumer sc35 |

The grill below decides each contested row.

---

## 3 · Grill 1 — Is in-neuron `Send` required, or is edge-only `Send` enough?

### Claim under attack

Module authors need `protected void Send(NeuronId to, Synapse fact)` inside a turn so
behaviors can target learned addresses (OS claim: "any behavior").

### Strongest argument FOR in-neuron Send

- North-star and sc02 both *look* directed: router → dashboard instance, not "every
  dashboard kind at my name."
- A behavior that learns an address from a fact body (e.g. `Source` of a prior reception,
  or an id embedded in a UI tap) has nowhere to put that address if only Emit exists.
- Without Send, authors invent fake fact kinds that only one instance declares — topology
  smuggled as vocabulary.

### Strongest argument AGAINST (Stage 1)

- Across FLOWS 1–10 and the north-star wiring path, **instance targeting is Connect table
  rows on the emitter**, not ad-hoc Send from handlers. Edge `Session.SendAsync` delivers
  `Connect` / directed edge speech. Answer paths use ask/reply routing, not Send.
- In-neuron Send is a **third resolution mode** authors will prefer over declarations —
  orchestration by address book, the anti-pattern OS.md forbids ("naming another module's
  class is orchestration").
- Learned addresses as free-form Send invite the virtual-actor silent world: any typo
  `NeuronId` "succeeds" into a parallel journal that never joins the real pipeline.
- `Send<TNeuron>` is already dead (type-coupled). Free `Send(NeuronId)` is the softer
  form of the same disease unless every call site is forced to prove the address was
  *learned from a fact/journal*, which Core cannot enforce.

### Counter-rebuttal (when Send returns)

The Kernel **behavior-creator** (not Core Stage 1) is the first honest consumer: it mints
a behavior instance name and must wire it without waiting for a new module ship. That
wiring is still better expressed as **journaled `Connect`** (rewire is a fact) than as
scattered Send calls. In-neuron Send earns a seat only if a real behavior proves Connect
cannot express a one-shot directed fact *and* declaration fan-out is wrong *and* reply
to source is wrong. None of the fifty scenarios force that triple.

### Decision

| Stage | In-neuron `Send` | Edge `Session.SendAsync` |
|---|---|---|
| **Stage 1 (now)** | **Absent** | **Present** — exact `NeuronId`, snapshot `to: [receiver]`, `via: ask` (directed), no declaration/connection fan-out |
| Later | Add only with a named Kernel consumer and a test that Connect/Reply cannot cover | Unchanged |

**Instance routing = Connect rows. Answer routing = Ask/Reply. Edge directed speech = Send.**

---

## 4 · Grill 2 — Is `Reply` a Core verb, or just "directed Emit"?

### Claim under attack

`Reply` is sugar: authors could `Emit` a reply-typed fact and Core would infer the asker;
or modules could Send-to-source if they could read the envelope.

### What "just directed Emit" would mean

1. **Return-value sugar only** (current disk answerer path): answerer `return new Greeted(...)`
   becomes a said entry with `Answers` stamped and asker forced into `to`. There is no
   module-callable `Reply`.
2. **Emit of TReply closes open ask** (deferred multi-turn): first later emission of reply
   type stamps `Answers` and adds the asker. Still no `Reply` verb.
3. **Modules read source and Emit/Send** — reintroduces ambient envelope as author API
   (`Handling`, Sequence, Cause). Correlation-as-API; defeated in architecture G8.

### Strongest argument FOR a first-class `Reply` verb

- OS.md and research define three resolution modes: Emit / Reply / Send. Two modes with
  one missing leaves "respond to who spoke to me" unexpressible for **non-ask** turns
  (e.g. edge `Send` of a command, listener wants a directed ack without inventing an ask
  pair).
- Reply must not require modules to see envelope fields. The verb is the privilege:
  Core reads turn-private source; modules pass only the fact body.
- Reply is still one bus: same commit → drain → Deliver. Only the receiver set differs
  (source ∪ declared overhearers of that fact type).

### Strongest argument AGAINST

- FLOWS 1/3/5 answer with **return** / deferred Emit of `TReply`, not `Reply(fact)`.
- A free `Reply` outside an ask can be abused as "directed broadcast with a friendlier
  name," skipping Connect discipline.
- Dual author surfaces for the same outcome (return vs Reply) confuse the algebra.

### Synthesis (defeat both extremes)

| Situation | Mechanism | Module surface |
|---|---|---|
| Answerer same-turn answer | Handler return `TReply` (or `IAnswers` return) | **return** — Core stages as reply-to-asker |
| Deferred answer | Later emission of `TReply` while open ask exists | **`Emit(replyFact)`** or **`Reply(replyFact)`** — both legal; Core stamps `Answers` once |
| Non-ask directed response to turn source | Must not open ask protocol | **`Reply(fact)` only** — Core-private source; no metadata leak |
| Ordinary announce | No special routing | **`Emit(fact)`** |

**Reply is a Core verb.** It is *not* a second transport. It is *not* module-visible
envelope access. It is address resolution: **turn source** (+ same-context overhearers of
the fact type under ordinary declaration rules).

**Return-from-answerer is not a competing philosophy** — it is the ergonomic form of Reply
for the ask protocol. Core implements both as the same staging path (`replyTo` /
`Answers` stamp + asker in `to`).

### Decision

- **`protected void Reply(Synapse fact)`** exists on `Neuron`.
- Ambient source is **Core-private** turn state. Modules never receive `SynapseMetadata`
  with Cause/Answers as a handling API (public read models may show identity; authors do
  not stamp lineage).
- Answerer **return** and deferred **Emit/Reply of TReply** share one closure rule.
- Modules cannot forge `Answers`; only Core stamps it.

---

## 5 · Grill 3 — Ask protocol: `IAnswers<,>` in Core vs bare `INeuron` + convention

### Options

| Option | Shape | Cost |
|---|---|---|
| A | Abstractions: `Synapse<TReply>` + `INeuron<TQ,TR>` | Fat ABI; dual interface in the only shared package; conflicts with P3 |
| B | Core: `IAnswers<TQ,TR>` + questions as ordinary `Synapse`; reply type on Core attribute / generic ask method | Thin ABI; reply pairing lives where Ask lives |
| C | Bare `INeuron<T>` only; convention "emit X means answer Y" | No boot cardinality; no `AskAsync` inference; overhearers make answerer selection undecidable |
| D | Keep dual `INeuron` but move answerer + `Synapse<TReply>` + `Answer<>` into Core | Same as B with different names; Abstractions stay four types |

### Attacks

**C fails hard.** Flow 6 overhearers declare `INeuron<FindTasks>` without answering.
Cardinality and routing cannot be recovered from "who implements INeuron of the
question" alone. Boot must distinguish listener vs answerer.

**A fails P3.** Architecture already relocated ask typing out of Abstractions. Disk code
that still carries dual `INeuron` + `Synapse<TReply>` in Abstractions is **pre-reset
debt**, not the ratified shape.

**Return-type `Task<Synapse?>` on a single interface** (historical rejection): null noise
on listeners, no compile-time pairing, no edge inference.

### Continuations

| Approach | When |
|---|---|
| Bare reply fact + `TState` join | Stage 1 default |
| Core-only `Answer<Q,R>` dispatch view (never journaled, never Emit-able) | If BDD ceremony counts prove TState boilerplate is real |
| Abstractions `Answer<>` | **Forbidden** |

Neurons that ask must declare how they continue: either `INeuron<TReply>` (hear the
reply as a fact) plus state, or a Core continuation registration the catalog can check
in-turn (today: `INeuron<Answer<,>>` on disk; post-reset equivalent lives in Core).
**Ask without a continuation declaration throws in-turn** (or edge session opts out
because journal reception *is* the close). Announce-only questions use `Emit`, never
`Ask`.

### Decision

- **Abstractions stay four types.** Ask pairing is **not** an ABI fork of every fact.
- **Core owns `IAnswers<in TQuestion, TReply>`** (name may match disk `INeuron<TQ,TR>`
  until reset; semantic is answerer role).
- **At most one answerer kind per question type** — boot fail on 2+.
- **`Ask` is a distinct verb** from `Emit`: answerer route + open-ask pin + continuation
  guard. `Emit(question)` is announce-only (listeners/overhearers; never answerer role;
  no open ask).
- **Questions are not Connect-able** (second answerer instance → duplicate replies).
- Edge: `AskAsync<TReply>` fires once, observes **session journal** for `Answers` match /
  `DeliveryFailed` / `AskExpired`. Task is volatile; journal is the ask.

---

## 6 · Grill 4 — Can we delete `Connect` and keep only declarations?

### The temptation

OS.md says "Nobody wires anything." Declarations are beautiful. Connect looks like v1
Subscribe with a new coat of paint.

### Ghost pipeline problem (load-bearing counterexample)

North-star: `XAccount` at name `"elonmusk"` Emits `XPost`. A behavior kind declares
`INeuron<XPost>` so it *can* receive posts. Without Connect:

```
Emit(XPost) @ elonmusk
  → every INeuron<XPost> kind at name "elonmusk"
  → Behavior@"elonmusk", Chart@"elonmusk", …  // ghost column
```

The owner wanted Behavior at a **minted instance name** (e.g. `"elon-btc-chart"`), not a
second brain living inside the account's locus. Declaration-only **forces** same-name
columns. Code-gen'd per-account kinds (ALT-2) explode the catalog and cannot rewire at
runtime without redeploy.

### What Connect is (and is not)

| Connect IS | Connect is NOT |
|---|---|
| Durable rows on the **emitter** (`factKind → NeuronId[]`) | A remote registry grain |
| Mutated only by journaled `Connect`/`Disconnect` through the ordinary bus | An emit-path RPC with timeout retract |
| **Ghost rule:** connection to kind K for fact F suppresses declared fan-out of F to K *at this emitter* | Dual derivation of who can hear (catalog still defines *capability*) |
| Validated at handling against **local catalog** → `ConnectionRefused` | String stream names / `[WireTo]` bodies |
| Cross-context and instance routes | A second delivery transport |

### Delete-Connect alternatives (rejected)

| Alt | Failure |
|---|---|
| Declarations only | Ghost pipelines; no runtime rewire; no cross-name instance |
| Connections only | Loses N+1 install without speaker changes; loses overhear; every route is manual |
| Kind-level runtime connections | Cannot mint parallel instances; loses locus isolation |
| Topology-registry neuron | v1 Subscribe repair + emit-path lookup |
| Install-time manifests only | Brain cannot rewire itself; behavior creator blocked |

### Scope honesty (ghost rule)

Before any connection exists and after `Disconnect`, declared same-context routes are
**live**. Pre-wiring emissions *would* hit ghosts. Practice: **Connect before
ingestion**. Disconnect journals `DeclaredRouteSurvives` when declaration would resume.
Muting a declared listener without Disconnect+redeploy is Revision-stage work, not a
silent mute API.

### Decision

**Keep Connect.** Topology = declaration capability ∪ connection instance wiring.
**Ghost rule stays.** No registry. No emit-path lookup. Authorization Stage 1 =
provenance (Source on Connect reception), not prevention; Kernel capability gate later.

---

## 7 · Grill 5 — Pub-sub many dashboards (sc47) without a second bus

### Scenario demand

Many panes (wall, mobile, chat sidebar) bind `IncidentOpened` / `IncidentClosed`.
Producer names nobody. Adding a fourth dashboard must not touch the producer.

### Wrong answers

| Wrong | Why |
|---|---|
| Orleans stream as n2n bus for incidents | Late join silent loss; dual truth with journals; sc10/28 misuse class |
| Global timeline of all facts | Documented silent loss; unbounded; audit dual truth |
| Producer holds `List<NeuronId>` in module state and Send loops | Orchestration; producer couples to consumers; survives badly under install |
| One mega-dashboard grain fan-out | Single journal bottleneck; kills independent layout evolution |

### Right answers on the one bus

**Same-context fan-out (default):** each dashboard kind declares `INeuron<IncidentOpened>`.
`IncidentDesk` Emits at a shared context name (e.g. `"ops"`). All declaring kinds at
`"ops"` hear. N+1 install = new kind + catalog epoch; speaker unchanged.

**Multi-instance same kind:** `Dashboard/wall-ops`, `Dashboard/mobile-glance` — different
**names**. Declaration fan-out alone does **not** reach other names. Options:

1. **Connect** each instance onto the desk's `IncidentOpened` row (owner/shell opens pane
   → edge `Send(Connect(...))` to the desk). Ghost rule applies per kind if a default
   same-name dashboard also declares.
2. **Shared context name** for all live dashboards of that surface (usually wrong —
   journals collide identity).
3. **Projection neuron** at the desk's context that Emits `UiSurface` variants; devices
   differ at the **edge** (SSE subscription id), not as separate n2n bus consumers.

sc47's multi-device reality is mostly (1) + edge SSE: neurons journal; **UiEdge** pushes
bytes. Streams/SSE are **egress after commit**, never authority.

### Delivery isolation

Slow dashboard ≠ stall others: per-receiver outbox progress, blocked-targets FIFO per
pair, independent activations. Poison one grain; others continue. At-least-once +
watermark; late activate does **not** replay history unless it **reads journals** or
Asks a snapshot (sc47 failure case — by design).

### Decision

**Pub-sub = Emit under declaration ∪ Connect.** No second bus. Edge streams project.
Many dashboards = many neuron ids, one fact kind, zero producer knowledge of consumer
count.

---

## 8 · Grill 6 — `DeliveryFailed` / self-heal as facts

### Physics

- At-least-once; receiver watermark; bounded retry for transient faults.
- Permanent faults (unknown kind, no handler) terminal on attempt **1**.
- Terminal outcome journals **`DeliveryFailed` on the sender** (not the receiver's
  private sorrow alone). Receiver may also journal terminal-unhandled for its truth.
- **Only Core** mints transport-truth `DeliveryFailed`. Modules that Emit a look-alike
  do not rewrite outbox reality (treat as ordinary vocabulary if ever allowed; prefer
  reserved emission).

### Self-heal (sc35)

```
Summarizer said SummaryReady → SlackPoster
  → retries exhaust
  → sender journals DeliveryFailed(fact, receiver, reason, attempts)
  → HealRouter : INeuron<DeliveryFailed>
  → Emit alternate path + UiSurface(degraded)
```

Healing is **composition**, not try/catch inside one mega-agent. Same pattern for
`ScheduleFailed`, `ConnectionRefused`, `AskExpired`.

### Attack: infinite heal loops

HealRouter fails → another `DeliveryFailed` → HealRouter… Cap with attempts, generation
in `TState`, or dead-letter fact after N heals. Core does not special-case; domain
policy does. Core guarantees **loud terminal facts**, not infinite smartness.

### Attack: forge DeliveryFailed

If modules can Emit Core outcome kinds as if they were transport, audit lies. Decision:
outcome kinds are ordinary **listenable** synapses; **outbox drain is the only writer of
transport DeliveryFailed**. Catalog may reserve emission of that kind to Core staging
paths (preferred). Modules listen; they do not mint transport failures.

### Decision

| Outcome | Where journaled | Listenable | Module emit |
|---|---|---|---|
| `DeliveryFailed` | Sender | Yes — self-heal | No (Core drain only) |
| `AskExpired` | Asker | Yes | No (Core expiry only) |
| `ConnectionRefused` | Emitter handling Connect | Yes (directed to requester) | No (Core validation) |
| `ScheduleFailed` | Scheduler neuron | Yes | No (Core schedule path) |
| `DeclaredRouteSurvives` | Emitter on Disconnect | Yes | No |

---

## 9 · Streams & "implicit subscriptions" (boundary grill)

### Allowed

| Use | Mechanism |
|---|---|
| UI / SSE push | Renderer or UiEdge observes committed facts / journal cursor → stream or HTTP |
| Telemetry mirror | Off committed facts; never authoritative |
| High-volume ingress | External bus → **adapter grain** → first `Deliver` + journal (sc28 wake) |
| Optional Stage-2 journal observer | One-way push to edge replacing poll; still secondary to journal |

### Forbidden

| Use | Why |
|---|---|
| Neuron A publishes stream as sole delivery to neuron B | Dual bus; late join; no watermark contract |
| Implicit stream namespace *instead of* catalog `INeuron<T>` | Topology invisible to journals; activation order bugs |
| Stream as transcript / causal history authority | Journals own truth |
| Late-subscribe correctness for history | Streams do not guarantee it; read journals |

### sc28 reinterpreted under physics

"Implicit stream wake" is valid **only** as: stream message → **ingress adapter** (or
Orleans implicit sub on an adapter grain) → adapter Emits/Delivers into the one bus →
dormant **neuron** activates on ordinary Deliver. The stream does not replace
declaration-is-subscription for neurons; it replaces **polling the outside world**.

---

## 10 · Broadcast naming

| Word | Meaning in this Core |
|---|---|
| **Broadcast** (prose) | Emit under declaration ∪ connection resolution; speaker names nobody |
| **`Broadcast` API** | **Does not exist** — second verb reintroduces dual fire paths (ino graveyard) |
| **Directed** | Reply (source), Send (edge exact id), or ask-role targets |
| **Pub-sub** | Same as broadcast prose + many listeners; not a product package |

CONTEXT.md's glossary entry for Broadcast is prose for Emit's default resolution mode,
not a second method.

---

## 11 · Attack log (compressed)

| # | Attack | Defense | Outcome |
|---|---|---|---|
| A1 | In-neuron Send required for OS | Connect + Reply + edge Send cover scenarios; free Send enables silent worlds | Stage-1: edge Send only |
| A2 | Reply is envelope disease | Source is Core-private; modules get verb only | Reply verb yes; Handling API no |
| A3 | Reply is pure sugar for return | Non-ask directed response has no return path | Reply verb retained |
| A4 | Dual INeuron must live in Abstractions | Thin ABI; ask is Core protocol | IAnswers / answerer in Core |
| A5 | Bare INeuron + convention for Ask | Overhearers break cardinality | Rejected |
| A6 | Delete Connect | Ghost pipeline; no runtime rewire | Keep Connect + ghost rule |
| A7 | Connect = v1 Subscribe | Local table, journaled mutate, no emit-path registry | Keep synthesis |
| A8 | Streams for sc47 scale | Per-receiver outbox + edge SSE | No n2n stream bus |
| A9 | Global timeline for overhear | Declare INeuron; read journals | No global bus |
| A10 | Same-turn reply ride-back | Second path bypasses outbox FIFO | Forbidden as correctness path |
| A11 | Emit collapses Ask | Answerer role + open ask + continuation guard | Ask stays distinct |
| A12 | Module-forged DeliveryFailed | Core drain is sole transport writer | Listenable, not forgeable |
| A13 | Neuron awaits AskAsync internally | Deadlock / occupation class | Only edge observes; neurons Ask + continue |
| A14 | Type-coupled Send\<TNeuron\> | Orchestration by class name | Dead forever |
| A15 | Zero receivers illegal | Uninstalled modules; introspection | `to: []` legal |

---

## 12 · RATIFIED — communication model

### Principles (locked)

1. **One causal bus:** commit said-entry with receiver snapshot → drain → direct `Deliver`.
2. **Resolution, not transports:** Emit / Reply / Ask / edge-Send differ only in how
   `to[]` is computed. One outbox, one watermark, one FIFO discipline.
3. **Declarations define capability; connections define instance wiring.** Ghost rule
   on the emitter.
4. **Ask is a protocol, not a fact-root fork in Abstractions.** Answerer interface and
   open-ask pins live in Core.
5. **Streams never own n2n truth.** Edge/ingress only; adapter journals first.
6. **Failure is vocabulary.** Terminal delivery/ask/topology/schedule outcomes are
   listenable Core synapses for self-heal composition.
7. **No neuron-awaits-neuron.** Continuations are later turns; edge Task is sugar over
   session journal.

### Final verb table

| Verb | Who | When (turn stage) | Receiver resolution | Journals | Notes |
|---|---|---|---|---|---|
| **`Emit(fact)`** | Neuron (in turn) | Stage only; leaves after commit | Declared listeners @ `emitter.Name` **minus** ghost-suppressed kinds **∪** `connections[factKind]`; self skipped | Said entry; `to[]` with `via: declared\|connected`; `to: []` legal | Default nervous system. Speaker names nobody. |
| **`Reply(fact)`** | Neuron (in turn) | Stage only | **Turn source** ∪ declared overhearers of fact type @ answerer/emitter name (ordinary rules); Core may stamp **`Answers`** when closing an open ask | Said entry; asker forced into `to` with ask-role when answering | Core-private source. Not envelope API. Return-from-answerer uses this path. |
| **`Ask(question)`** | Neuron (in turn) | Stage only | Catalog **answerer kind** @ **asker.Name** (ask-role); not Connect-able | Said entry + ask pin; open-ask registration; zero answerer → immediate `DeliveryFailed` | Requires continuation declaration (except edge session). Distinct from `Emit(question)`. |
| **`Send(to, fact)`** | **Edge `Session` only (Stage 1)** | Session turn commit | Exact `NeuronId` only — no declaration/connection fan-out | Said entry; `to: [receiver]` | Connect delivery, directed edge speech. **No in-neuron Send** until a named Kernel consumer forces it. |
| **`Session.EmitAsync`** | Edge | Session turn | Same as neuron Emit from session neuron | Session journal | Session **is** a neuron. |
| **`Session.AskAsync<TReply>`** | Edge | Fire ask once → **observe session journal** | Ask routing as above | Session journal is the ask | Matches `Answers` / `DeliveryFailed` / `AskExpired`. No same-turn correctness path. |
| **`Schedule` / `Unschedule`** | Neuron (+ fact forms) | Stage → schedule table | Ticks later self-deliver as ordinary facts | Schedule records + said trail | Not a second bus; time as facts. |

### Explicit non-verbs

| Name | Status |
|---|---|
| `Broadcast(...)` | **No API** — prose synonym for Emit resolution |
| `Send<TNeuron>(...)` | **Dead** |
| In-neuron `Send` | **Deferred** (not Stage 1) |
| Module `await otherNeuron` / internal AskAsync | **Forbidden** |
| Stream publish as n2n Deliver | **Forbidden** |

### Topology (ratified formula)

```
receivers =
    declaredListeners(exact type) @ emitter.Name
      EXCEPT kinds in connections[factKind] as targets     // ghost rule
  ∪ connections[factKind]
  ∪ { answerer@asker.Name } when emission is Ask
  ∪ { asker } when emission answers (Reply / return / deferred TReply)
  — dedup NeuronId; snapshot into said entry with via provenance
```

### Ask / answer (ratified)

| Rule | Detail |
|---|---|
| Answerer surface | Core `IAnswers<TQuestion,TReply>` (or equivalent); **not** Abstractions |
| Cardinality | ≤1 answerer kind per question type (boot) |
| Defer | Null/omit return keeps open ask; first later `TReply` emission closes |
| Continuation | Declare hear-reply / Core continuation; edge session journals only |
| Overhear | `INeuron<TQuestion>` listeners hear asks without answering |
| Connect | Questions refused (`ConnectionRefused`) |
| Horizon | `AskExpired` on asker; late reply journals, no continuation |

### Streams (ratified)

| Allowed | Forbidden |
|---|---|
| Edge SSE/UI projection after commit | Authoritative neuron→neuron stream |
| Ingress adapter → journal → Deliver | Implicit stream as catalog substitute |
| Telemetry mirrors | Late-join causal history via streams |

### Outcomes as facts (ratified)

| Synapse | Emitter | Consumer pattern |
|---|---|---|
| `DeliveryFailed` | Core drain (sender) | `INeuron<DeliveryFailed>` self-heal (sc35) |
| `AskExpired` | Core asker expiry | Timeout ledger / edge failure |
| `ConnectionRefused` | Core Connect validation | Behavior creator / BDD typo loudness |
| `ScheduleFailed` | Core schedule path | Reschedule / escalate |
| `DeclaredRouteSurvives` | Core Disconnect | Honest topology after unwire |

### Stage boundaries

| Stage 1 ships | Explicitly later |
|---|---|
| Emit, Reply, Ask, Schedule/Unschedule | In-neuron Send (consumer-gated) |
| Edge Emit/Send/AskAsync | Push journal observers replacing poll |
| Connect/Disconnect + ghost rule | Kernel capability gate on topology mutate |
| DeliveryFailed + listenable heal | Multi-open-ask keys if chat storms demand |
| Declarations + catalog fingerprint | Hot catalog Revision / ModuleActivated epochs |

---

## 13 · One-line algebra (author-facing)

**hear** (declare `INeuron<T>`) · **say** (`Emit`) · **ask** (`Ask`) · **answer**
(return / `Reply` / deferred `TReply`) · **wire** (`Connect`/`Disconnect`) · **heal**
(hear outcome facts) · **edge** (`Emit`/`Send`/`AskAsync` + read journals).

Streams and UI push sit **outside** that algebra as adapters. Anything that cannot be
expressed as a composition of the algebra is a signal to grill the *behavior*, not to
grow a second bus.

---

## 14 · Proof obligations (do not claim green without)

1. Emit zero-receiver journals `to: []` and does not throw.
2. Connect then Emit: only connected instance receives; same-context ghost kind does not
   (ghost rule).
3. Disconnect restores declared route and journals `DeclaredRouteSurvives` when applicable.
4. Ask routes to answerer @ asker name; `Emit(question)` does not.
5. Answer return and deferred TReply both stamp `Answers` and reach asker; overhearers of
   TReply still hear.
6. Edge `AskAsync` reconstructs from session journal after client restart (Task lost).
7. `DeliveryFailed` appears on sender after terminal failure; heal module can Emit
   alternate without Core special-case.
8. Session `Send` does not fan out to declarations.
9. No test uses streams as the sole path between two product neurons.
10. Root gate remains green; no same-turn reply ride-back as correctness.

---

*End of grill. Supersedes informal Emit/Send/Reply spreads in research where they
conflict with the Stage-1 verb table above; aligns with `CORE-ARCHITECTURE.md` §4 and
closes the Reply / Send / Connect contests for Core.*
