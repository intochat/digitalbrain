# Core as a mechanics framework — grill until it is not shit

**Status:** SUPERSEDES `plans/CORE-QUALITY-100.md` (that plan is product cosplay; trash).  
**Date:** 2026-08-06.  
**Motive:** Question **everything** — names, placement, verbs, special paths, types, tests — until Core feels like **Orleans / Aspire**: a small, inevitable instruction set. Modules ship **their own** neurons and synapses and align **organically** because the mechanics leave them no other wire.

**Non-goal:** XAccount, Gmail, WatchAccount, Elon, forms vocabulary inside Core docs as “north star.” That is **module space**. Teaching Core with those types is how v1 died.

**Goal:** One high-quality .NET framework package pair (`Abstractions` + `Core`) whose public surface is **pure mechanism**. Confidence comes from **mechanical proofs**, not story tests.

---

## 0 · What “organic alignment” actually means

A module author should only ever need:

1. Define **immutable facts** (`: Synapse`).
2. Define **activations** (`: Neuron` / `Neuron<TState>`).
3. **Declare** what they hear (`INeuron<T>`).
4. **Stage speech** with the closed verb set.
5. Register types in composition (`AddDigitalBrain(...)`).

Then:

- UI, Gmail, Salesforce, charts, buttons, multi-account watches are **just more modules**.
- They align because **declaration is subscription**, **Name is instance**, **journal is truth**, **delivery is post-commit** — not because Core knows “account enrich” or “OnTap.”

If a module needs a Core type named after a product domain, **Core is wrong**.  
If two modules only couple via **shared fact packages they both reference**, that is correct (like two Orleans grains sharing a DTO assembly — not the runtime shipping the DTO).

---

## 1 · Twenty grill iterations (everything is guilty)

Each iteration: **claim → attack → fold or hold → consequence for implementation**.

### Iteration 1 — “North star = sample module shape”

**Attack:** Any doc that starts with `XAccount : Neuron, INeuron<WatchAccount>` is teaching a **product**. Orleans does not ship `UserGrain` as the mental model of the runtime.

**Fold.** Delete sample domains from Core planning. North star = **grain of sand mechanics**: identity, turn, journal, route, deliver, bound failure.

---

### Iteration 2 — `Ask` / `IAnswers` feel per-project

**Attack:** Two hear shapes (`INeuron` vs `IAnswers`), two deliver methods (`Deliver` vs `DeliverQuestion`), open-ask pins, `AskExpired`, `AskFailedException`, continuation-by-reply-type — this is a **mini RPC product** bolted onto a bus. Modules learn “questions” as a second universe. Orleans has one invoke path; reply is just another message if you want it.

**Strongest defense:** ≤1 answerer + pin + Answers stamp is real protocol for “who may close this.”

**Grill harder:** That protocol can be **routing metadata on one Emit path**, not a second type system:

| Today (smells) | Mechanical alternative (grill candidate) |
|---|---|
| `Ask<TReply>(q)` | `Emit(q)` with **route mode** = singleton consumer by catalog role, or Connect-only |
| `IAnswers<Q,R>` | Still a **role marker** OR just `INeuron<Q>` that `Reply`s — catalog constraint “≤1 kind may Reply-close” |
| `AskExpired` | Generic **pin timeout** outcome, not “ask”-branded |
| Edge `AskAsync` | Edge **observe journal for Answers** — keep as host sugar, not Core identity |

**Consequence:** Either **justify Ask as irreducible mechanics** in ≤1 page with no product words, or **collapse Ask into Emit+route+pin**. Feeling “per project” means the brand is wrong even if the physics is useful.

**Provisional fold on branding:** names `Ask` / `IAnswers` / `AskExpired` are **shit product vocabulary**. Mechanics might stay; **names must become framework-native** (e.g. pin, sole-consumer route, timed pin release) — or collapse.

---

### Iteration 3 — `Reply` is a special snowflake

**Attack:** `Reply` = Emit directed to turn source. Why a verb? Why not `Emit` + directed address from ambient turn context only?

**Hold partial:** Directed-to-source is load-bearing and must not be forgeable as module-supplied Source.  
**Fold on API surface:** Could be one verb `Emit` with resolution modes: ambient fan-out | sole-consumer | to-turn-source | edge-named (Session only). Multiple methods that all call `StageSaid` are **API fat**.

---

### Iteration 4 — `Schedule` fact type vs `Schedule` verb

**Attack:** Type `Schedule` and method `Schedule` collide cognitively. `Unschedule(string Fact)` is stringly. Reserved intercept of module `INeuron<Schedule>` is a special case maze.

**Fold on cleanliness:** Framework should use **unambiguous mechanical names** (`Defer`, `PeriodWake`, `CancelWake`) or keep one spelling with zero dual meaning. String fact-kind in `Unschedule` is **shit typing** — prefer `Unschedule<T>()` only (already have verb; fact form is the smell).

---

### Iteration 5 — `Connect(string Fact, NeuronId To)`

**Attack:** `Fact` is a **string kind**, not `Type`/`Synapse` token. Typos are runtime (`ConnectionRefused`) — better than silent, still not Aspire-grade. Aspire uses strongly typed resource builders; Orleans uses `GrainId` strongly.

**Hold:** Kinds are strings forever (journal longevity).  
**Fold on API:** Connect should accept **compile-tied** tokens where possible (`Connect<TFact>(NeuronId to)`) generating kind from `TFact`, keep string overload only for edge/dynamic. Current string-first is **lazy**.

---

### Iteration 6 — `NeuronId.KindOf = type.Name.ToLowerInvariant()`

**Attack:** Rename class → durable address break. Collision on simple names. Feels toy.

**Hold:** String kinds are correct vs AQN.  
**Fold on quality:** Kind must be **explicit** (`[NeuronKind("x.account")]` or static `Kind` property) for framework-grade stability; convention-only is fine for demos, **wrong for OS**. Same for fact kinds.

---

### Iteration 7 — `Synapse` empty root

**Attack:** Marker-only is good (Orleans grain interfaces are markers too).  
**Hold.** Do not add `Synapse<TReply>` — that was correctly deleted.

---

### Iteration 8 — `INeuron<T>.HandleAsync` Task ceremony

**Attack:** Force async everywhere; sync handlers litter `Task.CompletedTask`. Prior design wanted `void Hear`. Ceremony is real.

**Fold candidate:** Support both or value-task; or source-gen. Today’s force-Task is **not** Aspire-polished.

---

### Iteration 9 — Public Core pack is half-mechanism half-story

| Type | Mechanical? | Verdict |
|---|---|---|
| `Connect` / `Disconnect` | Topology mutate | Keep (rename Fact→Kind if needed) |
| `ConnectionRefused` | Loud topology | Keep |
| `DeliveryFailed` | Transport terminal | Keep |
| `Schedule` / `Unschedule` / `ScheduleFailed` | Time table | Keep if rebranded cleanly |
| `AskExpired` | Pin timeout | **Rebrand or generalize** |
| `SynapseRef` | Journal pointer | Keep |
| `JournalFact` / `Delivery` / `NeuronReading` | Read model | Keep |
| `IAnswers` | Role | **Grill to death** (iter 2) |
| `AskFailedException` | Edge sugar | Host package, not Core identity? |

---

### Iteration 10 — `Brain` / `Session` naming

**Attack:** `Brain` is product mythology. Orleans has `IGrainFactory` / client. Aspire has `DistributedApplication`.  
**Session** collides with Orleans Serialization.Session (already NoWarn CA1724 — **smell**).

**Fold candidate:** `DigitalBrainClient` / `Context` / `Locus` / `Speech` — pick **boring framework names**. Cute names reduce open-source seriousness.

---

### Iteration 11 — `Neuron` inherits `DurableGrain`

**Attack:** Module authors inherit Orleans body. “No Orleans in modules” is a lie at the type hierarchy. Aspire doesn’t make you inherit DCP.

**Hold:** Implementation needs a grain.  
**Fold on packaging:** Split `DigitalBrain.Core` (public API, no Orleans types in public signatures) vs `DigitalBrain.Core.Orleans` (grain base). Public `Neuron` surface must not force `using Orleans` to read XML docs. Today is **not** clean framework layering.

---

### Iteration 12 — Fat `Neuron` partial god

**Attack:** 474+352+… LOC of turn+route+drain+schedule+ask in one type. Orleans Grain base is thin; runtime services own mechanisms.

**Fold.** Non-negotiable extract: **Outbox/Drain**, **Journal**, **Router**, **Turn pipeline** as internal types. Neuron = facade. Variable names, private fields, rehydrated caches — all grilled during extract (no `fact`/`stuff` soup).

---

### Iteration 13 — Catalog dual maps (listeners + answerers)

**Attack:** Two registration systems = two truths. Dual derivation graveyard pattern.

**Fold.** One declaration table; roles are attributes of a binding (hears / sole-consumer / …), not parallel dictionaries forever without a single model.

---

### Iteration 14 — Delivery is the product; tests don’t treat it as such

**Attack:** 49 scenario files vs thin delivery proofs. Frameworks prove **runtime contracts** (Orleans: activation, reentrancy, streams). We prove **Gmail stories**.

**Fold.** Root gate = mechanical suite only. Scenarios out of Core repo or `/samples`.

---

### Iteration 15 — String reasons on failures

**Attack:** `DeliveryFailed.Reason: string`, `ScheduleFailed.Reason: string` — untyped. Aspire/Orleans use structured errors more often.

**Fold candidate:** Closed reason codes + message; at least constants for depth, no-answerer, unknown-kind, timeout.

---

### Iteration 16 — Depth missing, Compact dead

**Attack:** Law claims depth; code lies. Compact exists uncalled. **That is not a quality codebase** — it is a blog post with a DLL.

**Fold.** Implement or delete law. Dead methods are **shit**.

---

### Iteration 17 — Edge `AskAsync` poll loop in Core

**Attack:** 75ms `Task.Delay` poll inside `Session` is host policy, not kernel.

**Fold.** Core exposes **journal cursor read**; host implements wait. Keeping poll in Core makes Core feel like a chat SDK.

---

### Iteration 18 — UI “capabilities”

**Attack:** Putting Form/Button/Chart in Core plan is the same disease as Gmail synapses in Core.

**Hold on requirement:** Owner must get forms/buttons/charts.  
**Fold on placement:** **Zero UI types in Core.** Mechanism: any `Synapse` is journal-visible; a **UI module** defines surfaces; Flutter **subscribes by declaration** (`INeuron<TUiFact>`) or reads journals. Organic alignment = **same Emit path**. No special UI bus.

---

### Iteration 19 — Multi-instance (many Twitter accounts)

**Attack:** Documenting `XAccount` as Core teaching is wrong.

**Hold on mechanism only:**

- Activation key = `(Kind, Name)`.
- Emit fans out by **declaration @ Name**.
- Connect rewires **per emitter activation**.
- Schedule is **per activation**.

Prove with kinds named `A`/`B`/`F` in tests — **never** brand names in Core tests.

---

### Iteration 20 — What “1000 subagents × 20” must mean

**Attack:** Spawning thrash does not create Orleans-grade code.

**Fold.** 20 **grill iterations** (this doc) define the bar. Execution = **small PRs of mechanics**, each with **mechanical tests only**, review standard: *would this look alien in the Orleans repo?*

---

## 2 · Framework surface target (aspirational clean)

After fold work, public mental model should fit on a card:

```text
IDENTITY    NeuronId(Kind, Name)     // explicit Kind preferred
FACT        Synapse                  // module-owned sealed records
HEAR        INeuron<T>               // declaration = subscription
ACTOR       Neuron / Neuron<TState>  // one turn; stage only
SPEAK       Emit (modes)             // fan-out | sole | to-source — grill Ask away or rebrand
TIME        Defer(period)            // self wake; no module timers
WIRE        Connect/Disconnect       // instance topology on emitter
EDGE        Client + Context         // send/emit/observe journal
TRUTH       Journal read model       // Cause/Answers/To structural
OUTCOME     DeliveryFailed, …        // listenable terminals only
HOST        AddDigitalBrain(...)     // composition = catalog epoch
```

**Modules never see:** GrainFactory, IDurable*, reminders, streams-as-bus, RequestContext, depth knobs.

**Modules always bring:** their synapses, their neurons, their DI services (HTTP, OAuth).

---

## 3 · Organic module alignment (including UI) — mechanics only

```text
                    ┌──────────────┐
   module facts     │   Synapse    │  (Gmail, UI, CRM — all equal)
                    └──────┬───────┘
                           │ declare INeuron<T>
                    ┌──────▼───────┐
                    │   Catalog    │  boot topology fingerprint
                    └──────┬───────┘
         Emit staged │     │ Name locus
                    ┌──────▼───────┐
                    │   Journal    │  sole causal truth
                    └──────┬───────┘
                           │ post-commit
                    ┌──────▼───────┐
                    │   Deliver    │  at-least-once, watermark, bounds
                    └──────────────┘
```

**UI:** UI module hears domain or assistant facts → Emits **its** surface facts → shell module hears those → edge mirrors to Flutter. Tap → edge Emits **module-defined** command facts. **Core unchanged.**

**Many accounts:** many Names, one Kind. **Core unchanged.**

**Gmail/Salesforce:** module packages; may share a **contracts** package between modules; **never** require Core to know MessageId.

---

## 4 · Implementation cleanliness bar (Orleans/Aspire grade)

| Rule | Shit (today-ish) | Clean |
|---|---|---|
| Public API | Ask-branded product RPC + Brain mythology | Boring mechanical names; minimal verbs |
| Layering | Neuron : DurableGrain public | API assembly free of Orleans; host binding separate |
| Types | string Fact kinds primary | generic Connect/Unschedule; explicit Kind attributes |
| Failure | free string Reason | structured codes |
| Dead code | Compact unused; depth law vapor | delete or ship |
| Tests | scenario novels in Core.Tests | `DigitalBrain.Core.Tests` = router/outbox/journal/catalog only |
| Samples | — | `samples/` may use X/Gmail; **not** Core |
| Comments | narrative essays | none unless invariant |
| Files | god Neuron | one mechanism one type |
| Naming | Session, Ask, Brain | no cute collisions; framework English |

---

## 5 · Mechanical test suite (only thing that builds confidence)

**Project:** `DigitalBrain.Core.Tests` (rename display: Physics).  
**Forbidden:** product words in type names inside this project.

| Suite | Proves |
|---|---|
| `CatalogTests` | kind collision, sole-consumer constraint, fingerprint, reserved |
| `RouterTests` | pure resolve: fan-out, ghost, zero receivers, multi-Name isolation |
| `OutboxTests` | FIFO, hole, attempts, horizon, depth bound |
| `JournalTests` | append, watermark, pin timeout, compact floor |
| `TurnTests` | throw → empty; poison on commit fail |
| `ConcurrencyTests` | reentrancy refuse, self-proxy |
| `EdgeTests` | context send exact; observe journal (no product Ask story) |
| `HostingTests` | durable key gate; silo boots |

**BDD optional** only if features speak **mechanics** (“outbox does not deliver before commit”), never “user enriches Salesforce.”

**Samples / scenarios:** separate project, not root confidence.

---

## 6 · Execution (no thrash agents)

1. **Rebrand / collapse decision on Ask** (grill iter 2) — design spike with pure mechanics doc § only; then code.  
2. **Extract Outbox+Router+Journal** from Neuron; green mechanical tests.  
3. **Ship or delete** depth + compact.  
4. **Explicit Kind** mechanism.  
5. **Strong Connect/Unschedule generics.**  
6. **Split packages** API vs Orleans body.  
7. **Rename edge** away from Brain/Session if we accept break.  
8. **Evict scenarios** from Core confidence gate.  
9. **Public API review** like a framework release (breaking changes OK on v2-core).  

Each step: smallest PR, mechanical tests only, “does this look like Orleans?” review.

---

## 7 · Explicit rejection list (do not do)

- Do not write another plan centered on XAccount/WatchAccount/PollX.  
- Do not add UiSurface to Core.  
- Do not add more product scenarios to “prove Core.”  
- Do not spawn hundreds of agents on the same tree.  
- Do not keep dead law text without code.  
- Do not teach Core with Gmail field names.

---

## 8 · One sentence

**Core is a fact-addressed, journal-true, post-commit delivery runtime with declaration topology and sealed durability — not an app template; modules bring all domain and UI types and align only by hearing and speaking facts.**

---

*If this still smells like a product, delete the smell: usually a name (`Ask`, `Brain`) or a sample type that should never have been in the Core conversation.*
