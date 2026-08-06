# Core root-cause grill — eight parallel audits, one architecture diagnosis

**Date:** 2026-08-06  
**Method:** 8 concurrent explore agents (Neuron, Delivery, Catalog, Ask, Edge, Journal, Tests, Public ABI). Read-only. Code is truth.  
**Rule:** when something is wrong, go **one level up** — do not propose local renames as the fix.  
**Supersedes as north star:** `CORE-QUALITY-100.md` (product cosplay). Complements `CORE-MECHANICS.md` with **evidence**.

---

## 0 · Verdict (read this first)

DigitalBrain Core is **not** “almost Orleans-grade with missing tests.”

It is a **real journaled delivery bus** with **several speech universes glued into one type (`Neuron`)**, **laws that exist only in markdown**, and a **test suite that greens product stories while the kernel hardness is unproven**.

Modules will not “organically align” until Core is a **closed mechanical machine** with one speech model, one commit authority, one package split (module / edge / host), and a **physics-only root gate**.

**Confidence today for “attach any module and it just works”:** low (~30–40%).  
**Not because green count is low** — because green count is the **wrong proof**.

---

## 1 · Root causes (architecture level only)

These are the **parents** of almost every concrete defect the agents found. Fix parents; children disappear.

### R1 · One type is three planes (speech / delivery / edge)

**Evidence:** Neuron partials own turn verbs, outbox drain, Session edge, schedule ticks, Connect intercept; multiple `CommitCoreBatchAsync` callers; Session bypasses `RequireTurn`.

**Root:** Kernel planes are not type boundaries. Partials fake modularity.

**Until this is fixed:** every special-case (Ask vs Emit vs Schedule vs Connect) stays load-bearing and hostile.

---

### R2 · One function approximates every verb (`StageSaid`)

**Evidence:** Flags `AskAnswererKind`, `AskLacksAnswerer`, `DirectedTo`, open-ask scan, `replyTo`; Via first-wins; Emit of reply type can steal open asks; `Reply()` does not stamp Answers; Session.Send stamps Via=`ask`.

**Root:** There is no closed speech algebra in code — there is a **flag soup compiler**.

**Until this is fixed:** “Emit / Ask / Reply / Send” is documentation fiction.

---

### R3 · Delivery mode is reconstructed, not stored

**Evidence:** `questionRoute = Via==ask && Answers==null && IsQuestion` — Session.Send of a question-shaped fact becomes DeliverQuestion; Via means three things.

**Root:** Said entry does not carry **role/mode**; runtime sniffs decorations.

**Until this is fixed:** edge and module speech will keep colliding.

---

### R4 · Outbox is not a state machine

**Evidence:** journal rows + in-memory unsettled + exception taxonomy + two timers + string reasons; no depth; permanent handler bugs = transient retry; `wakeupArmed` lies; self-delivery nests full turns on drain stack.

**Root:** Missing **typed delivery outcomes + durable next-work schedule + private hop budget**.

**Until this is fixed:** storms, sticky bugs, and wake gaps are structural.

---

### R5 · Journal lifecycle is half-built

**Evidence:** `Compact` never called; soft caps cannot bound heard-after-cursor; tallies/Reset never leave transport Read ABI; schedule tick = two commits (effect then NextDue → duplicate risk); watermark Touched not refreshed on silent dup.

**Root:** Ratified durability **diagram** wired for short tests, not long-lived log.

**Until this is fixed:** “journal is OS truth” is true for greeters, false for 30-day brains.

---

### R6 · Topology identity is demo-grade

**Evidence:** `KindOf = type.Name`; fingerprint logs only (no join refuse); dual listener/answerer maps; Connect string Fact + open Name mint; ghost is all-or-nothing per kind; Name overloaded for owner/device/rev/account.

**Root:** Durable identity and multi-silo safety treated as conventions, not sealed OS schemes.

**Until this is fixed:** multi-module and multi-silo are hope.

---

### R7 · Edge teaches product RPC; package graph lies

**Evidence:** Brain/Session names; 75ms poll in Core; AskAsync as only typed wait; IGrainFactory on public ctor; no client hosting; ReadOnly without AlwaysInterleave; Neuron:DurableGrain public; modules share Core DLL with host+edge types.

**Root:** Core is three products in one assembly; edge dialect is chat-shaped.

**Until this is fixed:** every module author sees Brain/AskAsync and builds wrong systems.

---

### R8 · Proof suite inverted

**Evidence:** ~24 physics vs ~59 scenario cosplay; P18/P19 depth missing and **depth not in code**; Compact untested; P04 Disconnect missing as sole law; multi-owner tests rebrand Name locus as multi-tenant; FINAL LAW unpaid.

**Root:** Expressibility elevated to confidence gate.

**Until this is fixed:** green CI is a **lie about kernel hardness**.

---

## 2 · Fatal shortlist (do not start modules on top of these)

| # | Fatal | Parent |
|---|---|---|
| F1 | Via=`ask` overload + questionRoute sniff | R2, R3 |
| F2 | StageSaid flag soup / open-ask type steal | R2 |
| F3 | Reply() not Answers | R2 |
| F4 | Session-as-Neuron out-of-turn speech | R1, R7 |
| F5 | Dual ask maps, no single ask aggregate | R2 |
| F6 | Schedule tick nested commit + synthetic sequence | R1, R5 |
| F7 | MaximumDepth law absent in code | R4 |
| F8 | Compact dead + floor cannot bound hear traffic | R5 |
| F9 | Fingerprint does not gate silos | R6 |
| F10 | Read interleave incomplete under long turns | R7 |

---

## 3 · What is actually sound (so we don’t throw the bus)

| Keep | Why |
|---|---|
| Abstractions 4 types, zero deps | Framework-grade |
| Journal-as-outbox, post-handler stage, poison on write fail | Correct durability sketch |
| Watermark at-least-once + abandonment commit before unblock | Sound if classify/FIFO completed |
| Declaration fan-out + ghost rule + Connect on emitter | Right topology family |
| Ask **protocol** (sole answerer, pin, Answers, no neuron-await) | Justified mechanics — **not** a second bus; surface/branding/debt is the problem |
| Seals: obsolete GrainFactory/WriteStateAsync, concurrency refuse, durable key gate | Right direction |
| Session-as-neuron for durable edge speech | Hold the *job*; fix the *bypass* and packaging |

**Agent disagreement resolved:** Ask machinery **stays as protocol**; Ask **branding and Via soup** do not. Collapse verb into Emit+role is rename-level; deleting pin/Answers/sole-answerer is wrong.

---

## 4 · Organic modules (your real goal) — what Core must be

A module ships:

```text
sealed record SomeFact(...) : Synapse;
sealed class SomeNeuron : Neuron, INeuron<SomeFact> { ... Emit / Schedule only ... }
```

It aligns **only if**:

1. One speech model (roles on said entry, not flag soup).  
2. Kind/Name identity is stable and explicit.  
3. Delivery outcomes are typed and bounded (depth, permanent vs transient).  
4. Journal grows and prunes under a real lifecycle.  
5. UI is **another module’s synapses**, never Core types — same Emit path.  
6. Multi-instance is **Name** (and Connect), proven with abstract kinds in Core tests — never XAccount in Core.  
7. Public module IntelliSense does **not** show Brain/Session/AskAsync.

That is “Orleans-like”: small instruction set, you bring domain.

---

## 5 · Execution order (parents first)

Do **not** write more scenarios. Do **not** invent XAccount north stars.

| Order | Work | Kills |
|---|---|---|
| 1 | Implement **said role/mode** + stop Via sniff; fix Session.Send stamp | F1, R3 |
| 2 | Split **StageSaid** into explicit stage ops or delete open-ask Emit steal | F2, F3, R2 |
| 3 | **MaximumDepth** private + terminal outcome codes | F7, R4 |
| 4 | Outbox outcomes: permanent vs transient vs backpressure (not catch Exception) | R4 |
| 5 | Wire **Compact** + fix floor for settled heard; expose Reset on read or delete tallies | F8, R5 |
| 6 | Schedule: one commit for tick effect + NextDue (or non-watermarked timer turn) | F6 |
| 7 | Explicit Kind (attribute/static); fingerprint join gate or stop claiming multi-silo | R6, F9 |
| 8 | Package split: Core (Neuron+outcomes) / Edge / Hosting; move poll out of Core | R7 |
| 9 | AlwaysInterleave on committed reads | F10 |
| 10 | Invert tests: physics-only root gate; scenarios out of confidence | R8 |
| 11 | Extract Delivery + Journal + Router from Neuron (after algebra is honest) | R1 |

Each step: mechanical tests only. Red before green. No product nouns in Core.Tests.

---

## 6 · Agent report index

| Agent | Focus | Strongest finding |
|---|---|---|
| Neuron turn | verbs, StageSaid, Session | Two speech universes + StageSaid is every verb |
| Delivery | outbox, drain, policy | No state machine; depth vapor; permanent=transient |
| Catalog | KindOf, ghost, fingerprint | Fingerprint is a slogan |
| Ask | IAnswers protocol | Keep protocol; clean special cases |
| Edge | Brain/Session/host | Product RPC dialect in Core library |
| Journal | Compact, tallies, schedule | Compact dead; schedule dual-commit |
| Tests | suite architecture | Expressibility ≠ hardness |
| ABI | public surface | Split packages; Abstractions is clean |

Full narratives live in the subagent transcripts (session tools). This file is the **merged root map**.

---

## 7 · One sentence

**Core fails as a framework not because modules need more concrete synapses, but because the kernel itself has not finished becoming one speech algebra, one delivery machine, one durable log lifecycle, and one honest package boundary — and the tests celebrate stories while those machines remain incomplete.**

---

*Generated from live code audits 2026-08-06. Prefer delete. Prefer root. Prefer mechanics.*
