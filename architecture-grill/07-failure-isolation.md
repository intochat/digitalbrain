# 07 · Failure modes, isolation, security provenance

Date: 2026-08-05. Status: **RATIFIED** Core guarantees (testable).  
Inputs: `CORE-DESIGN.md` physics #1–#4/#6–#7, turn pipeline §4, `CORE-ARCHITECTURE.md`
(G8, G18, G23, non-goals), `CONTEXT.md` (single-owner brain / deployment isolation),
scenarios 25, 27, 35, 36, 37, 42, 43, v1 trap class (Drain↔Deliver reentrancy, depth 16),
live Core (`NeuronConcurrency`, `DeliveryPolicy`, filters, poison path).

Method: state a recommendation, strongest counter, defend or fold. Prefer delete. One
mechanism per job. Every claim below either is already physics or is a checklist row a
cluster test can fail on.

---

## 0 · Scope — what this grill is for

Core is the nervous system physics. Failure, isolation, and provenance are **not** product
features layered on top of a chat stack; they are either structural (the runtime makes the
bad path inexpressible) or explicitly **out of Core** (policy neurons, share gateways,
egress gates).

| Concern | Core owns | Modules / Kernel / edge own |
|---|---|---|
| Delivery never silent-lost | Yes — `DeliveryFailed` on **sender** | React / heal / surface |
| Causal Source identity | Yes — stamped only by Core | Never set, never spoof |
| Reentrancy deadlock class | Yes — structural absence | Handlers stay non-reentrant |
| Causal chain depth bound | Yes — ported budget | Design graphs under the bound |
| Poison after ambiguous commit | Yes | Retry against fresh activation only |
| Owner / multi-owner tenancy | Stage 1: **deployment** isolation | Kernel IdP, product multi-brain, share policy |
| Prompt-injection policy | No (body is data) | TrustTagger, EgressGate, capability broker |
| Share pane vs journals | Journals never auto-share | ShareGateway + guest session + redaction |
| Business auth / capability tokens | No | Kernel capability gate |

If a row would require Core to "understand" email text, OAuth principals, or UI share
tokens, it is **not** a Core guarantee — force it into a module and keep Core dumb.

---

## 1 · Reentrancy deadlock class

### Physics (already proven, do not re-open)

v1 trap: `DrainAsync` awaited `Deliver` **inside** the emitting neuron's turn while
`NeuronConcurrency.RequireSerializedTurns` forbids reentrancy → hang.

v2 structural defense (all load-bearing):

1. **Nothing leaves before commit** — return of `Deliver` means committed staging, never
   "answer arrived."
2. **Neurons never await neurons** — continuations are later turns (`INeuron` / open-ask
   join on journal).
3. **Drain is a separate serialized turn** — timer/reminder, post-commit; never inside the
   handler that staged the emission.
4. **Self-delivery is a direct method call** — never the grain proxy.
5. **Outgoing filter** converts proxied self-calls into a loud exception (deadlock → fail).
6. **Boot refusal** of `[Reentrant]`, `[MayInterleave]`, module `[AlwaysInterleave]` on
   mutating surfaces, extra grain interfaces, module `IRemindable`.

### Recommendation

Keep the class **structurally impossible**. Do not add "safe reentrancy" flags, sync
ride-back, or in-turn remote registry. Nested workflows (sc37) are multi-turn fact joins,
not nested grain awaits.

### Strongest counter

"Edge HTTP needs same-turn answer for timeout budgets; chat tools need nested awaits for
latency."

### Defense / fold

Fold the latency complaint into **edge journal observation** (`AskAsync` polls session
journal for reply / `DeliveryFailed` / `AskExpired`). Fold the tool chain into **open asks
+ TState join**. Accept latency of one extra hop. Same-turn reply as correctness path was
FATAL (bypasses outbox FIFO → deterministic loss under retry). **Stand.**

### Grill

| Attack | Defense | Decision |
|---|---|---|
| Handler `await`s peer neuron | No public API; no GrainFactory; verbs only stage | Stand |
| Drain inside open turn | Drain only on `IDrainEntry` / timer path after commit | Stand |
| Self-proxy "for convenience" | Outgoing filter throws | Stand |
| Script VM `await brain.CallAsync` | Scripts are neurons speaking facts (G18) | Stand |
| Nested ask as stack (sc37) | Open ask + later reply turn; join in TState | Stand |
| Interleave Deliver with long model IO | Model IO is **in-turn local await**; peer Deliver still post-commit | Stand; interleave only committed **reads** if ever |

---

## 2 · Depth (causal chain budget)

### Conflict with `03-journal-durability.md`

Doc 03 **deleted** hop-depth, claiming cycle control is structural and that unbounded
chatter is bounded by retry horizon + attempts. That claim is **half-right and half-fatal**:

| Mechanism | Stops failed delivery loops? | Stops **successful** reaction storms? |
|---|---|---|
| Retry horizon / attempts | Yes — each hop eventually `DeliveryFailed` | **No** — successful Emit→handle→Emit never retries |
| Open-ask backpressure | Same-kind concurrent asks | Not broadcast listener graphs |
| "No self on declaration path" | Direct self-fan | Multi-kind cycles A→B→C→A still live |
| Module rate limits | Optional | Optional; Core cannot assume every module has one |

sc36 ("script infinite Emit loop") and connection cycles need a bound that fires on
**successful** hops. Retry physics never see them. **03's delete is overturned here** for
storm control; 03 remains correct that depth is **not delivery identity** (dedup stays
`(Source, Sequence)` only) and that `EmitAtDepthAsync` must never return.

### Recommendation

Port v1 **MaximumDepth = 16** as Core **storm / cycle budget**, not identity:

- Each said emission carries private `depth = parentDepth + 1`.
- Edge-born and pure schedule-born facts start at depth **1**.
- Drain that would deliver past max journals terminal
  `DeliveryFailed(…, reason: exceeded maximum synapse depth)` on the **sender** on
  attempt **1** (permanent — no horizon burn).
- Depth is Core-private (outbox/progress or said-entry field). **Not** on Abstractions
  `SynapseMetadata`. **No** module API to read, set, or reset depth (kills v1
  `EmitAtDepthAsync` budget restart).
- Dedup, Cause, Answers, and FIFO remain independent of depth.

### Strongest counter

"16 is arbitrary; deep agent graphs and heal cascades will hit it; put rate limits in
modules instead." / "03 already deleted it to avoid gaming."

### Defense / fold

- Gaming required a **module-visible** depth API. Delete the API; keep Core-stamped
  carry only — same pattern as Source.
- Deep legitimate graphs that need >16 hops are a product design smell; raise the
  constant with a measured consumer and a green test, not by removing the guard.
- Module rate limits are still required for **external** effect storms (email send
  loops); Core depth is the structural backstop for the fact bus.
- **Stand at 16 for Stage 1.** Amend 03's depth paragraph: hop-depth returns as
  non-identity storm control; horizons stay for **failed** delivery.

### Grill

| Attack | Defense | Decision |
|---|---|---|
| Depth on public `SynapseMetadata` | Authors stamp / reset → dual truth | Core-private only |
| `EmitAtDepthAsync` / out-of-turn reset | No verb; every emission in-turn; Core stamps only | Delete v1 escape hatch |
| Depth only in RequestContext | Lost on redelivery | Persist on said outbox shape |
| Heal on `DeliveryFailed` restarts depth | Listener inherits cause depth; new edge-born heal starts at 1 only from edge/schedule | Tests for cascade |
| 03: "horizon bounds chatter" | Successful multi-kind cycles never hit horizon | Overturn for storms |
| Legitimate 20-hop plan | Raise constant with consumer proof; do not delete bound | Bound stays |

**Note:** current `DeliveryPolicy` has attempts/horizon but **not yet** depth —
checklist B1–B5 are the ratification contract; implement before claiming sc36 guards.
Update `03-journal-durability.md` C6 when editing that doc next (depth is storm control,
not wire identity).

---

## 3 · `DeliveryFailed` (and the failure family)

### Recommendation

Core journals delivery outcomes as **ordinary listenable synapses** on the **sender**
(physics #4). Closed Core vocabulary (append-only):

| Fact | Where journaled | Meaning |
|---|---|---|
| `DeliveryFailed(Fact, Receiver, Reason, Attempts)` | Sender | Exhausted retry / permanent refuse / depth / no-answerer |
| `ConnectionRefused(…)` | Emitter receiving bad Connect | Topology validation failed |
| `AskExpired(Ask, Question)` | Asker | Delivery ok, no answer in AskHorizon |
| `ScheduleFailed(…)` | Scheduling neuron | Tick failures hit limit |

Modules may `INeuron<DeliveryFailed>` for self-heal (sc35). Modules **cannot** mint a
transport-true `DeliveryFailed` for another neuron's outbox: only Core's drain stages it.
A module that *Emits* a record shaped like failure is just another fact if that type is
module-owned; Core kinds for outcomes are Core-produced (reserved production path —
see provenance §5).

### Strongest counter

"Journal failure on the receiver too, so the victim has local audit."

### Defense / fold

Receiver journals **its** truth: reception unhandled / refusal. Sender journals **its**
truth: outbox hole closed. Cross-link is `SynapseRef`. Dual journal of the same terminal
with different writers is dual truth. **Stand — sender owns DeliveryFailed.**

### Terminal vs transient (do not re-litigate)

| Class | Behavior |
|---|---|
| Permanent (unknown kind, no declared handler, depth exceeded, zero answerer on Ask) | Terminal on attempt **1**; `DeliveryFailed` |
| Transient (timeout, poison-reload race, network) | Retry under `MaximumAttempts` / `RetryHorizon`; then `DeliveryFailed` |
| Handler throw (incl. cancel) | **Zero durable trace on receiver**; sender retries; eventual `DeliveryFailed` |
| Commit fault | Poison; no emission left the neuron; redelivery converges |

### Grill

| Attack | Defense | Decision |
|---|---|---|
| Silent abandon (v1 telemetry-only) | Always journal terminal | Stand |
| Forge `DeliveryFailed` as transport truth | Only drain stages Core outcome; Source = emitting neuron | Stand |
| Heal infinite loop on DeliveryFailed | Depth + Attempts + module must gate; Core does not special-case heal | Module owns loop policy; Core owns depth |
| Abandonment without commit barrier | Commit terminal hole **before** unblocking FIFO (FATAL a2) | Stand |
| Drain-commit failure swallowed by timer | Poison + rethrow path | Stand |

---

## 4 · Poison (ambiguous commit = refuse activation)

### Recommendation

On **any** failure of the single turn/drain `WriteStateAsync`: set poison flag,
`DeactivateOnIdle`, rethrow. Every entry point (`Deliver`, drain tick, ask-expiry,
compaction, schedule tick) checks poison first and throws/no-ops until a **fresh**
activation reloads committed truth. No retraction commit, no in-memory compensation.

### Strongest counter

"Retry the write in-place; poisoning is operationally noisy."

### Defense / fold

Write-landed-ack-lost is indistinguishable from write-failed. In-place retry can double
mutate if the write landed. Poison+reload is the only correct answer under ambiguous
commit and deletes v1 retraction machinery. **Stand.**

### Grill

| Attack | Defense | Decision |
|---|---|---|
| Handler throw poisons | No — discard staging only; sender retries | Stand |
| Poison only Deliver, not drain | Drain commit faults equally poison (timer swallow) | Stand |
| Module clears poison flag | Flag is private; no module surface | Stand |
| Reads while poisoned | Committed-only reads may interleave if `[AlwaysInterleave]` on Core read surface only; never mutate | Core-owned surgical interleave only |

---

## 5 · Security provenance — Core must not forge `Source`

### Recommendation (load-bearing for injection and multi-owner)

**`Source` is the identity of the activation that committed the said entry.** Core mints
it from the grain's own `NeuronId` at commit. Modules never pass Source into verbs.
`Reply` / `Emit` / `Ask` / `Schedule` take **bodies only**. Public metadata is identity
for **readers**; authors never stamp it.

Transport path:

- Outgoing filter writes envelope from **sender's staged delivery** (`TakeOutboundDelivery`).
- Incoming filter consumes headers and refuses delivery without envelope.
- Journal is authority; RequestContext does not survive storage/redelivery as truth.

**What Core does *not* do:** decide whether email body text is trusted. That is policy
modules (sc43). Core's job is: when GmailIngress emits `EmailReceived`, **Source is the
ingress neuron**, not "the attacker," not a forged chat owner, not a spoofed assistant.

### Strongest counter

"Put trust level on SynapseMetadata so every module sees it."

### Defense / fold

Trust on the envelope becomes a second author API and will be forged or forgotten.
Trust is a **fact** (`ContentUntrusted`, `CapabilityDenied`) produced by a policy neuron
whose Source is that policy neuron — overhearable, journaled, testable. **Stand — no
trust field on Abstractions metadata.**

### Grill

| Attack | Defense | Decision |
|---|---|---|
| Module sets Source to "owner" / "system" | No API; Core stamps Id | Stand |
| Mutate RequestContext mid-turn to spoof | Filters consume/write Core-owned keys; journal overrides on rehydrate | Stand; tests |
| Connected non-answerer impersonates reply | Answer reconstruction requires catalog answerer kind + Answers stamp + shape fingerprint | Stand (anti-fabrication) |
| Edge injects forged owner header | Stage 1: no OwnerId in NeuronId; edge session context is Name; auth is edge | Edge must not trust client-supplied locus without session bind |
| Core mints DeliveryFailed with foreign Source | Outcome is said by the sender; Fact ref points at original | Stand |
| Prompt injection claims "I am Source=assistant" | Body text is data; Source remains ingress/assistant activation | Policy modules; Core provenance intact |

---

## 6 · Owner isolation & multi-owner

### Stage-1 ratified model (`CONTEXT.md`, G23)

- **One owner per deployment / brain.** Isolation between owners = separate deployments
  (separate silos, catalogs, storage), not a tenant key inside Core addresses.
- **Within a brain**, isolation units are **neuron identity** `NeuronId(Kind, Name)` and
  **journals**: context Name (locus) partitions conversations and instance state.
- `OwnerId` is **forbidden in Abstractions** and is not Stage-1 Core address space.

### Recommendation

Do **not** put multi-tenant OwnerId into Core grain keys for Stage 1. Scenarios 25/27
pass as: (a) two deployments, or (b) product Kernel later — not dual identity schemes in
Core now.

### Strongest counter

"Cost forces shared silo multi-tenant; scenarios already assume it."

### Defense / fold

Shared-silo multi-tenant without Kernel identity design recreates dual keys (Owner+Kind+Name),
stream namespace bugs, and filter forge games. When product forces it, Kernel designs
partitioning **once**; Core gains an explicit epoch, not a silent Owner field on every
fact. **Stand Stage-1 deployment isolation.** Document sc25/27 as **acceptance language
for product**, not as Stage-1 Core implementation claim.

### Within-brain isolation Core *does* guarantee

| Guarantee | Mechanism |
|---|---|
| Context A journals ≠ context B | Separate activations `kind@name` |
| Declaration fan-out stays same-context | Catalog locus rule |
| Cross-context only via Connect | Local emitter table + validation |
| No global unscoped bus | No global timeline; streams edge-only |
| Session read is named neuron | `Brain.ReadAsync` by NeuronId |

### Grill

| Attack | Defense | Decision |
|---|---|---|
| Tenant in NeuronId now | Dual identity + ABI bloat | Reject Stage 1 |
| Broadcast without context partition | Catalog fans out at emitter Name only | Stand |
| Journal query "all emails in silo" | No Core API; modules answer from own journals | Stand |
| Shared stream id across owners | Streams not n2n; if edge streams used later, namespace includes brain/deployment | Product |
| Stateless worker cache by kind only | Workers non-authoritative; must key by owner/brain if multi-deploy | Module/service |

---

## 7 · Share pane, not journals (sc42)

### Recommendation

**Core has no share primitive.** Journals are the owner's nervous system. Collaboration
that shares *pixels* is a **module + edge** path:

1. Owner context emits `UiSurface` (or similar) as ordinary facts.
2. ShareGateway / PolicyRedactor produce **derived** guest facts into a **guest session**
   (separate Name / edge session), never raw journal replay APIs.
3. Guest Ask into owner neurons is refused by topology / edge auth (no Connect grant).
4. Audit of grant/revoke lives in the **owner** journal only.

Core guarantees that make share-without-journals *possible*:

- Journals are per-neuron; there is no "brain-wide export" bus.
- Edge can open a session Name that is not the owner's desk and only hear what is
  delivered into it.
- `ConnectionRefused` / missing Connect keeps guests out of owner neurons.

Core does **not** guarantee redaction quality, TTL tokens, or PII stripping — modules.

### Strongest counter

"Core should offer `ShareJournalSlice` for compliance and support."

### Defense / fold

Legal hold / support elevating is Kernel break-glass with audit (sc14), not a casual share
API. Putting share on Core invites "share journals by default." **Stand — no Core share
of journals.**

### Grill

| Attack | Defense | Decision |
|---|---|---|
| Guest MCP `read_neuron_journal` | Edge auth scopes tools to session; Core Read still needs addressability | Edge/Kernel deny |
| Guest Connect to owner chat | Connect validation + no privilege inheritance | Module/edge policy |
| UiSurface embeds raw email | Redactor module default-deny unknown blocks | Module |
| Token after expiry | Guest session Unschedule/Disconnect; Core does not store share tokens | Module |

---

## 8 · Adversarial: prompt injection via email (sc43)

### Split of responsibility (ratified)

| Layer | Duty |
|---|---|
| **Core** | Deliver `EmailReceived` with Source = GmailIngress; body is opaque JSON; no eval; no privilege escalation from text; capability is facts not free text |
| **Policy modules** | Tag untrusted content; deny egress/tool acts whose only authority is untrusted text; journal `CapabilityDenied` |
| **Assistant module** | Feed model **safe views**; structured tool proposals only |
| **Egress / capability** | Single answerer gates high-impact acts; owner confirm is a new high-trust fact |

### Recommendation

Core must remain **injection-agnostic**. If Core grows "sanitize email" it becomes a
policy engine and freezes the wrong seam. Prove Core with: Source unforgeable + journals
preserve untrusted body as data + no second wire that elevates body to code.

### Strongest counter

"Without Core-level taint tracking, modules will forget."

### Defense / fold

Taint-in-Core is metadata bloat and incomplete (calendar, attachments, OCR). Force
**composition**: marketplace/Kernel requires TrustTagger for ingress packs; tests in module
suites. Core proves provenance physics. **Stand.**

### Grill

| Attack | Defense | Decision |
|---|---|---|
| Body says "Source=system" | Source is activation Id | Core |
| Body says "Emit DeliveryFailed" | Text is not dispatch | Core |
| Model tool call from injection | Broker accepts structured module proposals only | Module |
| Behavior auto-forward on keyword | EgressGate + marketplace scan | Module/Kernel |
| Silent deny without audit | Policy must journal denial | Module (Core provides journal bus) |

---

## 9 · Core guarantees vs module obligations (summary)

### Core guarantees (physics)

1. Commit-before-dispatch; no same-turn reply as truth.
2. No neuron-awaits-neuron; no Drain↔Deliver reentrancy class.
3. At-least-once delivery; watermark dedup on `(Source, Sequence)`.
4. Terminal failure journaled on sender as `DeliveryFailed` (and family).
5. Poison on commit ambiguity; reload committed truth.
6. Source/sequence/timestamp/cause/answers minted by Core, not modules.
7. Serialized turns; second wires refused (filters + concurrency boot).
8. Depth budget (16) on causal delivery chains (port; see checklist).
9. Per-neuron journals as sole causal truth; no global unscoped bus.
10. Stage-1 one owner per deployment; within-brain isolation by `NeuronId` locus.

### Modules must handle

1. Self-heal policy on `DeliveryFailed` / `ScheduleFailed` (loop caps, alternate routes).
2. Trust tagging and egress policy for untrusted external text.
3. Share/redact/guest projection (never "ship journals").
4. Domain cancel/replan facts (committed emissions are not un-said).
5. Correct Connect targets learned from facts (virtual actors mint parallel worlds).
6. Idempotent external effects (journal records committed turns, not every attempt —
   capability seam Stage 2 journals effects later).
7. Multi-owner product semantics beyond deployment isolation (Kernel).

### Core must never

- Forge or accept module-supplied Source.
- Treat synapse body text as privileged instruction.
- Provide unscoped cross-context / cross-owner journal dump APIs.
- Await remote Deliver inside an open handler turn.
- Silently drop terminal delivery failure.
- Put OwnerId / capability tokens / trust scores on Abstractions metadata.

---

## 10 · RATIFIED Core guarantees checklist

Each row is a **testable assertion**. Owning suite: `DigitalBrain.Core.Tests` (public API /
cluster) unless marked Kernel/edge. A green product scenario does not replace these.

### A · Reentrancy & concurrency

| ID | Assertion | Proof sketch |
|---|---|---|
| A1 | A neuron type annotated `[Reentrant]` / `[MayInterleave]` / module mutating `[AlwaysInterleave]` **fails boot** with asserted message | `NeuronConcurrency.RequireSerializedTurns` contract tests |
| A2 | Module type implementing extra `IAddressable` grain interface **fails boot** | Concurrency contract |
| A3 | Proxied self-call throws naming the self-delivery rule | Outgoing filter test |
| A4 | Self-delivery of schedule tick / ask expiry uses direct path and completes under serialized turns | Schedule + ask expiry tests |
| A5 | Handler that only `Emit`s then returns never deadlocks; peer receives after commit | Greeter / emit round-trip |
| A6 | Nested ask workflow completes without nested grain await (ask → later reply turns) | Multi-turn ask join test (sc37 shape) |

### B · Depth

| ID | Assertion | Proof sketch |
|---|---|---|
| B1 | `DeliveryPolicy.MaximumDepth == 16` (single constant) | Unit / policy test |
| B2 | Emission chain of length 17 at a receiver fails terminal with reason containing depth bound; sender journals `DeliveryFailed` | Cluster chain of listeners |
| B3 | Depth is preserved across redelivery (not reset by RequestContext alone) | Fault-inject mid-drain |
| B4 | Edge-born fact starts depth budget at 1; child emits are depth+1 | Journal/outbox depth field assert |
| B5 | Depth-exceeded is **attempt 1 terminal** (no 30-min burn) | Clock + attempt count on failure record |

### C · DeliveryFailed & failure family

| ID | Assertion | Proof sketch |
|---|---|---|
| C1 | After bounded retry exhaustion, sender journal contains exactly one terminal `DeliveryFailed` for that `SynapseRef`+receiver | Sticky fault receiver |
| C2 | Unknown grain kind / no handler → `DeliveryFailed` on attempt 1 | Missing kind composition |
| C3 | `Ask` with zero answerers → immediate `DeliveryFailed(no-answerer)` | Catalog without answerer |
| C4 | Handler throw → receiver journal **unchanged** for that delivery; sender eventually terminals | Throw listener + fault |
| C5 | Abandonment: receiver not unblocked for later seq until `DeliveryFailed` commit for the hole | Crash between attempts + FIFO assert |
| C6 | Module `INeuron<DeliveryFailed>` receives Core-journaled failure (self-heal hook) | Listener composition (sc35) |
| C7 | `AskExpired` when answer never arrives within AskHorizon; late reply does not dispatch continuation | Controllable clock |
| C8 | Permanent refuse and transient timeout are distinguishable by Attempts/Reason patterns in tests | Dual fault kinds |

### D · Poison

| ID | Assertion | Proof sketch |
|---|---|---|
| D1 | Failed turn commit → next Deliver throws poison; after deactivation, redelivery converges; no receiver saw uncommitted emit | Sticky journal fault |
| D2 | Failed **drain** commit poisons identically | Fault on abandonment write |
| D3 | Write-landed-ack-lost → watermark swallows duplicate; no double handler run | Ambiguous commit injector |
| D4 | Poison flag not clearable by module code (no public API) | API surface / reflection ban if needed |
| D5 | Handler throw does **not** poison | Explicit negative test |

### E · Provenance / Source integrity

| ID | Assertion | Proof sketch |
|---|---|---|
| E1 | Said entry Source equals emitter `NeuronId`; never a verb parameter | Emit + ReadAsync |
| E2 | Delivery without envelope headers is refused (kernel bug, not soft fail) | Incoming filter |
| E3 | Redelivered fact rehydrated from journal bytes matches first delivery body | Codec equality |
| E4 | Reply-type impersonation via Connect does not close ask / dispatch continuation | Anti-fabrication conjunct |
| E5 | Question shape fingerprint mismatch → terminal, no fabricated members | Drift test |
| E6 | No public module API to set Source, Sequence, Cause, or Answers on outbound facts | ABI / Neuron surface review test |
| E7 | Reserved Core outcome kinds (`DeliveryFailed`, …) are produced by Core paths; modules declaring `INeuron` for reserved **topology** kinds (`Connect`/`Disconnect`/`Schedule`/`Unschedule`) fail boot | Catalog boot refusals |

### F · Isolation (within brain + Stage-1 multi-owner stance)

| ID | Assertion | Proof sketch |
|---|---|---|
| F1 | Two contexts (`desk-a`, `desk-b`) interleave asks; each journal holds only its conversation | Flow 8 style |
| F2 | Declaration fan-out does not deliver into a foreign context Name | Catalog locus |
| F3 | Zero-receiver emit journals `to: []` and delivers nowhere | Composition without listener |
| F4 | `Connect` typo / non-declaring target → `ConnectionRefused`; table untouched | Connections feature |
| F5 | No Core type `OwnerId` in Abstractions | Publish-gate / ABI test |
| F6 | Stage-1 claim: multi-owner shared silo is **not** a Core green bar; documented as deployment isolation | Architecture acceptance (this doc + G23); product tests live elsewhere |

### G · Share / journals / adversarial data

| ID | Assertion | Proof sketch |
|---|---|---|
| G1 | Core exposes no API that bulk-exports another context's journal without addressing that NeuronId | Edge/Brain surface inventory test |
| G2 | Guest-shaped session Name that never receives owner emits sees empty/no owner facts | Two-session test |
| G3 | Fact body may contain injection strings; they never change Source or open a second wire | Hostile body round-trip |
| G4 | Emitting hostile text does not execute as code in Core | Same; no eval path |
| G5 | Streams are not required for n2n delivery; disabling stream providers does not break Deliver/outbox | Composition without streams |

### H · Bounds & liveness (regression net)

| ID | Assertion | Proof sketch |
|---|---|---|
| H1 | `MaximumAttempts`, `RetryHorizon`, `DeliveryAttemptTimeout`, `AskHorizon=2×RetryHorizon` match design constants | Policy equality tests |
| H2 | Unsettled outbox + idle activation resumes via reminder without inbound traffic | Restart survival |
| H3 | Per-(sender,receiver) order preserved across retry and dual drain race | Ordering feature |
| H4 | Watermark pruning still rejects duplicates within horizon | Dedup feature |
| H5 | Split-brain: stale activation commit refused by fencing storage | Two-writer test |

---

## 11 · Explicit non-claims (do not "pass" these as Core)

| Non-claim | Owner |
|---|---|
| Email body cannot influence model tool choice | Assistant + policy modules |
| Shared-silo multi-tenant hard isolation | Kernel / product (post Stage 1) |
| Guest cannot social-engineer owner into Connect | Edge UX + policy |
| Heal always finds alternate channel | HealRouter module |
| PII redaction completeness on shared panes | PolicyRedactor |
| Legal hold completeness | Compliance module + Kernel break-glass |
| Prompt-injection "solved" | Never a Core checkbox |

---

## 12 · Decision log (this grill)

| # | Decision | Stand / fold |
|---|---|---|
| 1 | Reentrancy deadlock class remains structurally impossible | Stand |
| 2 | Causal depth budget 16, Core-private storm control (not identity); overturns 03's hop-depth delete | Stand (implement if missing) |
| 3 | `DeliveryFailed` only on sender; listenable; never silent | Stand |
| 4 | Poison+reload on commit ambiguity; no retraction | Stand |
| 5 | Source unforgeable; no trust/Owner fields on Abstractions metadata | Stand |
| 6 | Stage-1 multi-owner = deployment isolation | Stand (G23) |
| 7 | Share pane is modules; Core does not share journals | Stand |
| 8 | Prompt injection policy is modules; Core is provenance + data-as-data | Stand |

---

## 13 · Implement-before-claim gap

Relative to this ratification, verify in tree and close if open:

1. **Depth** on said emissions + drain terminal (v1 port; mentioned in design table, not in
   current `DeliveryPolicy` constants).
2. Checklist rows B1–B5 green under `DigitalBrain.Core.Tests`.
3. Explicit tests for E6 (no module Source API) and G3 (hostile body).
4. Do not mark sc25/27 as Core Stage-1 done without either deployment-isolation proof or a
   separate Kernel multi-tenant design grill.

---

*Prefer delete. If two mechanisms protect isolation, keep the structural one. Journals are
the audit; policy is a neuron; Source is Core.*
