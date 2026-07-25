# Behavior OS residual grill — Who emits `DigitalBrainActivated`?

Wave: **B0** · Agent: **4** · Mission: `design-behavior` (emitter lock only)  
Write scope: this file only (no `design.md` / code edits)  
HEAD at grill: `7ffaa21a415ed676ea4735cab06fa2de29a2b4d4`  
`git status --porcelain`: clean (empty)

Vision restatement: **Framework = neurons + synapses; OS = behaviors (including UI); activation is a synapse Flutter reacts to.**

Primary question:

> Who commits the broadcast fact `DigitalBrainActivated` (name final after vocab grill) into the owner journal so an OS behavior/composition can react and start UI through `IShell` / `SceneOpened`?

Related residual (not this grill’s home): synapse package home (Contracts vs Abstractions) — agents 3 / 13–16.  
Related residual (downstream): who **reacts** (`BootOnActivation` name already appears in Explicit BDD) — agents 5–6 / B3.  
Related residual (invoke site in production before rail): who **calls** the emitter — hold honest; not AppHost business rules.

---

## Codegraph paste (mandatory)

### Query 1

```
ISessionNeuron Emit Fire DigitalBrainClient Connect AddDigitalBrainClient session entry gateway
```

**What it does (1 sentence):** Owner-bound client is constructed by `Connect` / `AddDigitalBrainClient`; all product `EmitAsync`/`SendAsync` enter through `ISessionNeuron` (`Session().Emit` / `Session().Fire`); session is gateway, not a domain product emitter.

**Call path:**

1. `AddDigitalBrainClient` → `DigitalBrainClient.Connect(IGrainFactory, owner)` → `new DigitalBrainClient` (no journal, no grain call)
2. `IDigitalBrain.EmitAsync` → `Session().Emit(synapse)` → `Neuron.EmitAsync` (broadcast catalog + subscriptions + journal)

**Callers / consumers:**

| Symbol | Role |
| --- | --- |
| `DigitalBrainClient.Connect` | DI factory in `AddDigitalBrainClient`; Testing/host wiring; `[EditorBrowsable(Never)]` |
| `IDigitalBrain.EmitAsync` | Public broadcast entry for compositions, edges, future behaviors |
| `ISessionNeuron.Emit` / `Fire` | Session substrate only; **not** addressable via `Get`/`SendAsync` (client rejects session as Send target) |
| `SessionNeuron` | Kernel impl of gateway; domain-free |

**Blast radius if Connect/session auto-emitted activation:** every DI resolve / test Connect would become a product fact; Kernel or Client would learn OS vocabulary; dual with deliberate compositions.

**Dual paths:** none for product activation today — **activation fact does not exist on disk** (string residual in Explicit tests only).

**Public vs internal:** `IDigitalBrain` / `DigitalBrainClient` public; `ISessionNeuron` substrate (`[ClientEntryPoint]`); `SessionNeuron` internal Kernel.

**Framework vs OS vs edge:** Client + session = **framework substrate**; must not own OS boot policy.

### Query 2

```
SceneOpened EmitAsync OpenHome PostAuthBootstrap composition shell RunAsync Activate module capsule
```

**What it does (1 sentence):** Pre-rail OS logic is pull-invoked sealed compositions over `IDigitalBrain`; UI facts (`SceneOpened`) are emitted by **Flutter neurons** after composition calls `IShell.Open`; module `ICompiledModule.Activate` is silo DI registration, not owner activation.

**Call path (home open):**

1. Test → `new OpenHome().RunAsync(brain, shellName, ct)`
2. `brain.Get<IShell>(shellName).Open(OpenScene(...))`
3. `ShellNeuron` → `EmitAsync(new SceneOpened(...))`
4. Ui edge SSE projects `SceneOpened` (does not invent it)

**Callers / consumers of compositions:** composition **tests only** today (`ShellAndSurfaceCompositions`, honesty/boot residuals). Architecture §5: not installed into production silo, not host startup.

**Module capsule Activate:** `DigitalBrainSiloBuilderExtensions` → `module.Activate(ISiloBuilder)` for selected modules — grain/services registration. **Zero** `IDigitalBrain`, **zero** owner journal.

**Dual paths:** `OpenHome` vs `PostAuthBootstrap` both open home (Explicit residual honesty). AppHost has **no** open-home call — correct.

**Framework vs OS vs edge:** compositions = **pre-rail OS**; `IShell`/`SceneOpened` = **module vocab**; Ui SSE = **edge projection**.

### Query 3

```
OpenHome RunAsync ShellAndSurfaceCompositions PostAuthBootstrap who invokes composition tests
```

**What it does:** Confirms compositions are **pull-invoked** witnesses — the only Built pattern for “OS logic runs” without Behavior rail.

**Dependents:** `PostAuthBootstrap` / `OpenHome` → composition tests + Explicit Behavior OS residuals.

**Public vs internal:** composition classes public sealed; identity = namespace + class (future Behavior identity per §5).

---

## Candidate grill (all five)

### 1) `IDigitalBrain.Connect` / session grain first touch

| | |
| --- | --- |
| **Claim** | Wiring or first session activation emits `DigitalBrainActivated` automatically |
| **Evidence against** | `Connect` is pure construction (`new DigitalBrainClient`); contracts pin Connect as wiring only. Session `OnActivateAsync` only recalls handled deliveries / outbox wake — no product synapses. Grain reactivation would re-fire unless Kernel stores “already activated” durable OS state. |
| **Kernel domain?** | Yes if session emits named OS fact — **forbidden** |
| **Verdict** | **Reject** |

**Strongest counter for auto-session:** “activation *is* the owner session becoming live — framework fact.”  
**Fold that counter:** liveness of Orleans grain ≠ product OS boot. MCP/Ui/DI construct clients without intending first-screen boot. Framework fact would still need a durable once-flag in Kernel or a domain type in Kernel — both lose.

### 2) Explicit composition `ActivateDigitalBrain.RunAsync` → `brain.EmitAsync(fact)` — **RECOMMENDED**

| | |
| --- | --- |
| **Claim** | Pre-rail sealed composition (future Behavior identity) deliberately broadcasts the activation fact through the existing client entry |
| **Evidence for** | Architecture §5 OS composition before the rail; same shape as `OpenHome` / `PostAuthBootstrap`; `EmitAsync` is the ratified broadcast door; Explicit BDD already names activation → `BootOnActivation` **reaction** as separate step; honesty test forbids host Program special-case |
| **Kernel domain?** | No — composition uses `IDigitalBrain` + contracts/Abstractions only |
| **Host Program rules?** | No — invoker residual separate; emitter itself is ordinary C# |
| **Verdict** | **Lock for B0 design** |

Body sketch (not implemented this cycle — design only):

```csharp
// identity = namespace + class; future Behavior home
public sealed class ActivateDigitalBrain
{
    public Task RunAsync(IDigitalBrain brain, CancellationToken cancellationToken)
    {
        // cancel check + null guards
        return brain.EmitAsync(new DigitalBrainActivated(/* owner-ambient; payload grill residual */));
    }
}
```

**Who reacts (not emitter):** separate composition/behavior (Explicit name `BootOnActivation`) handles/reacts to the fact → `IShell.Open` → `SceneOpened`. Do not collapse emit + open into one god composition long-term (dual product sentence risk with `OpenHome` / `PostAuthBootstrap` — already residual).

**Who invokes emitter pre-rail (residual hold):**

| Invoker | Status |
| --- | --- |
| BDD / composition tests | **Required first consumer** (pull-invoke — matches Built pattern) |
| Scripts / ordinary host code holding `IDigitalBrain` | Allowed — programming model honesty |
| Ui edge HTTP bind | **Not** automatic (see candidate 3) |
| AppHost / silo `Program.cs` | **Forbidden** product special-case |
| Behavior install rail | **Designed** — when Built, external synapse activation still applies; emitter may itself become approved Behavior |

Idempotency (emit twice): residual for B1/B3 — prefer journal-visible deliberate re-emit over hidden Kernel dedupe until a real consumer requires once-only.

### 3) Ui edge on HTTP bind

| | |
| --- | --- |
| **Claim** | When Ui process listens / first request, emit activation |
| **Evidence against** | Architecture: edges project vocabulary, no business logic. Ui already maps open-scene / control-activate / SSE; bind is process lifecycle, not owner OS decision. Headless / MCP-only / composition tests would need a second emitter (dual path). |
| **Verdict** | **Reject** as emitter |

**Allowed later (not emit):** edge may **invoke** the composition after auth binds owner (post-auth composition entry) — still composition emits, edge does not invent the fact. That invoker is residual and must not embed open-home logic in `UiEndpoints`.

### 4) AppHost / `Program.cs` special case

| | |
| --- | --- |
| **Claim** | AppHost or host `Program` calls open-home / emits activation |
| **Evidence against** | Product AppHost today is module + edge projection only (`WithUiEdge`/`WithFlutterHost`). Explicit residual: “activation synapse drives boot — not host Program.cs”. Campaign forbids host hand-wire theater. |
| **Verdict** | **Reject** |

### 5) Module capsule `Activate`

| | |
| --- | --- |
| **Claim** | Generated `ICompiledModule.Activate(ISiloBuilder)` emits owner activation |
| **Evidence against** | Activate = silo builder DI/grain registration; no owner, no journal, no `IDigitalBrain`. Runs once per silo process for selected modules — wrong cardinality (modules ≠ owners). |
| **Verdict** | **Reject** |

---

## Recommendation form (lock)

```
Recommendation: invent + emit via pre-rail composition ActivateDigitalBrain
  (or architecture-final name) calling IDigitalBrain.EmitAsync(DigitalBrainActivated);
  reaction stays a separate OS composition/behavior (BootOnActivation residual name).
  Reject Connect/session auto-emit, Ui bind emit, AppHost Program emit, module capsule Activate.

Strongest argument against:
  Something must still call RunAsync before the rail; compositions are not host-startup-wired
  today — production UI may never see activation unless invoker is designed; auto-session
  would “always work” without an invoker.

Defense / fold:
  Defend. “Always work” by hiding emit in Connect/session is false safety: it fires on
  wiring, reactivates with grain lifecycle, and forces Kernel/Client OS domain knowledge.
  Architecture already requires pull-invoke honesty for compositions; Explicit BDD already
  fails without the chain; invoker residual is smaller and honest. Prefer explicit fact +
  explicit reaction over host magic.

Evidence:
  codegraph Connect = construct only; Emit path = Session gateway;
  OpenHome pattern + composition tests pull-invoke;
  architecture §5 Behaviors + OS composition before rail;
  Explicit BehaviorOsActivationBoot / Honesty residuals;
  AppHost.cs zero product open/emit.
```

### Scoring rule hits (§1)

1. Framework purity — session/client stay substrate; no OS emit in Kernel  
2. Behavior OS honesty — emitter is composition → future Behavior identity  
4. Synapse activation — broadcast fact, not name-dispatched runner  
5. BDD — Explicit product sentence already names commit of activation fact  
6. Architecture alignment — §5; no fake Built rail  
7. Encapsulation — Kernel domain-free  
11–12. Grill + codegraph first  

---

## Target chain (B0 lock shape)

```
owner-scoped IDigitalBrain available (Connect / AddDigitalBrainClient / TestBrain — wiring only)
  → ActivateDigitalBrain.RunAsync(brain)          // EMITTER (this grill)
  → brain.EmitAsync(DigitalBrainActivated)        // journaled broadcast via session
  → BootOnActivation (composition/behavior)       // REACTOR (other agents)
  → IShell.Open(OpenScene(...))
  → SceneOpened                                    // module neuron fact
  → Ui edge SSE / Flutter projects first screen    // edge only
```

**Not built.** Do not claim Built. Emit type + package home remain vocab residual.

### Name residuals (out of emitter lock)

| Item | Hold |
| --- | --- |
| Exact record name (`DigitalBrainActivated` vs architecture rename) | Vocab / design doc |
| Package home (Abstractions substrate vs module Contracts) | Prefer **not Kernel**; Abstractions only if multi-module OS identity requires substrate-wide type; else dedicated OS/contracts home — agents 3 / 13–16 |
| Payload fields (owner redundant?, correlation, reason) | Thin fact; owner ambient on session journal |
| First screen key (login vs home) | Flutter-react agents 5–6 |
| Collapse vs keep `PostAuthBootstrap` / `OpenHome` dual | Honesty residual — boot reaction should own one first-screen path |

---

## Grill board (13)

1. **What does this thing do?** Deliberately broadcasts owner-scoped OS activation as a typed synapse so UI/OS logic can react without host special-cases.  
2. **Layer?** **OS behavior (pre-rail composition)** emitting through **framework** `EmitAsync`; fact type = vocab residual (not Kernel).  
3. **Consumer today?** Explicit BDD strings + campaign design (Wave B0); no runtime consumer yet — **honest zero-consumer until red proofs land**.  
4. **Architecture placement?** §5 Behaviors (“synapse activation externally”); §5 OS composition before rail; §4.6 Flutter reaction via vocabulary facts — **deliberate extension** of activation fact (not silent invent of Behavior rail).  
5. **UI synapse path?** Flutter reacts to **`SceneOpened`** (Built); activation fact is **upstream OS boot signal**, not a Dart widget type. Vocabulary open = `IShell` / `OpenScene`.  
6. **If deleted?** No product break today (unbuilt). If deleted after lock: Explicit activation BDD cannot green; dual risk of AppHost/Ui inventing boot.  
7. **Invent install rail?** **No** — ordinary sealed class + `EmitAsync`; no `IBehavior`, no approval API.  
8. **Kernel domain word?** **No** under composition emitter. Yes under session auto-emit — rejected.  
9. **Proof shape?** BDD + journal (Explicit `BehaviorOsActivationBoot` already); not compile-only.  
10. **Claimed without command?** Did not run root gate (docs-only cycle). Codegraph + architecture + Explicit tests + AppHost read are the oracles used.  
11. **Foreign dirty tree?** None at start (`porcelain` empty; HEAD `7ffaa21a…`).  
12. **One layer in/out?** In: composition emit (OS). Out-wrong: Kernel session, edge bind, AppHost, module Activate. Framework supplies **pipe** only (`EmitAsync`).  
13. **New engineer from vision + architecture alone?** **Yes after this residual is accepted into design doc:** vision says activation synapse; §5 says compositions own logic pre-rail; §5 rule 22 synapse activation externally — emitter = composition that emits, not host.

---

## Assess template (§6)

```
Scope: Who emits DigitalBrainActivated (B0 design lock; residual grill file only)
Codegraph query + blast radius (paste §3):
  Q1 session/client entry — Connect wiring-only; Emit→Session gateway; no product activation
  Q2 SceneOpened/compositions/module Activate — OpenHome pull-invoke → ShellNeuron Emit;
     ICompiledModule.Activate = silo DI only
  Q3 composition invokers — tests only today
What it does (1 sentence):
  Explicit pre-rail composition broadcasts owner activation via IDigitalBrain.EmitAsync
Layer: os-behavior (pre-rail composition) + framework emit pipe; fact type = vocab residual
Consumer today: Explicit BDD residuals / design campaign (no green product consumer)
Architecture home (section): §5 Behaviors + OS composition before the rail; emit via client §5/§ programming model
Activation synapse?: DigitalBrainActivated (name hold) — invent with red Explicit (already failing)
Flutter reaction path: activation → BootOnActivation residual → IShell.Open → SceneOpened → Ui SSE
BDD scenario (Given/When/Then):
  Given DigitalBrain is activated for an owner
  When the activation synapse is committed
  Then a Flutter OS behavior/composition reacts
  And the UI starts
  And the first screen is presented (journal SceneOpened evidence)
Public surface:
  New composition class (samples/Compositions or future Behaviors home) + synapse record (vocab home TBD)
  No new IDigitalBrain members; no Connect side effects; no AppHost API
Implementation hidden? Y — Kernel/session remain domain-free; edge remains projection
Belongs here? Y as OS composition emitter; N on Connect/session/Ui bind/AppHost/module Activate
Aligns with framework=neurons+synapses, OS=behaviors? Y
Dual path / host hand-wire? Reject AppHost/Program and Ui-bind emitters; residual OpenHome vs PostAuthBootstrap dual separate
Delete candidates: none this cycle (no code); future delete host-side open-home if any appears
Recommendation form (§2): composition emitter lock — see above
Verify: docs-only; no build/test claim; Explicit residuals already encode sentence
Grill 13: complete above
```

---

## B0 design-lock statement (copy into design doc when agents 13–16 merge)

**Lock:** The emitter of `DigitalBrainActivated` is an **explicit pre-rail OS composition** (`ActivateDigitalBrain` or final name) that calls **`IDigitalBrain.EmitAsync`**. It is **not** `Connect`, session first touch, Ui HTTP bind, AppHost/`Program.cs`, or module capsule `Activate`.

**Separate locks (do not conflate):**

- **Reactor** composition/behavior opens first screen via Flutter vocabulary.  
- **Invoker** before Behavior rail = tests + deliberate ordinary C#; never silo module Activate; never AppHost product rules.  
- **Fact type home** = vocab grill (not Kernel).

**Fold condition (when to reopen):** only if a red proof shows a **substrate-wide** activation fact must be emitted with **zero** OS composition present (pure multi-edge cluster with no compositions package) **and** Kernel can emit without learning a domain type (generic mechanism) — not the current product sentence.

---

## Out of scope / did not do

- No code, no package moves, no design.md edit (write scope = this residual only)  
- No root gate  
- No live aspire claim  
- Did not finalize synapse package home or first-screen key  
