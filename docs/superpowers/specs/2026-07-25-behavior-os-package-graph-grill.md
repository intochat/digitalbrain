# Behavior OS — package graph grill (2026-07-25)

**Mission:** Wave B0 agent 6 · `design-behavior`  
**Scope of this file:** package *homes* only — where activation, pre-rail OS logic, the future install
rail, and activation→UI BDD proofs live. No product C# invented here. No `IBehavior` packages.  
**Vision restatement:** Framework = neurons + synapses; OS = behaviors (including UI); activation is a
**broadcast synapse** that Flutter vocabulary reacts to — not a host `main()` special case and not a
name-dispatched “run behavior X” API.

**Authority (in order):** `CLAUDE.md` · `docs/architecture.md` (§§1–3, 4.6 Flutter, §5 Behaviors) ·
`docs/packages.md` · hosting/testing design 2026-07-24 · architecture-aligned mass deletion ·
ownership scorecard Hold #8 / #14.

**Accelerators:** codegraph used for Client `EmitAsync` / `Synapse` / compositions / Testing surface;
`docs/packages.md` and L0 boundary tests for the compositions graph. Context7 not required (no new
package/framework API authored).

---

## 0. Package graph ground truth (today)

### Packable product inventory (`docs/packages.md`)

| Layer | Packages | Role relevant to Behavior OS |
| --- | --- | --- |
| Framework leaf | `DigitalBrain.Abstractions` | `Synapse` base; domain-neutral capability facts (`CapabilityRequested` / Completed / Failed / Rejected); `IHandle<T>`, neuron contracts bases |
| Framework runtime | `DigitalBrain.Kernel` | Journal, broadcast catalog, neuron mechanics — **no domain / no UI / no Behavior install product API** |
| Client programming model | `DigitalBrain.Client` | `IDigitalBrain`: `Get` / `SendAsync` / `EmitAsync` only |
| Testing | `DigitalBrain.Testing` | `DigitalBrainFixture` / `TestBrain` / AppHost fixtures — **no `IBehavior` / `IBehaviorTest`** |
| AppHost | `DigitalBrain.Aspire.Hosting` | Durable brain composition; module selection — **not OS product logic** |
| Modules | `*.Contracts` + runtime (+ optional Aspire.Hosting) | Domain vocabulary only (e.g. Flutter `IShell` / `SceneOpened`) |
| Metapackage | `DigitalBrain` | Abstractions + Client + Aspire consumer — never Kernel/modules |

### Not NuGet / not product packages

| Tree | Graph (compile refs) | Role |
| --- | --- | --- |
| `samples/DigitalBrain.Compositions` | **Client + Abstractions + Flutter/Time/AI Contracts only** (L0 `CompositionBoundaryContracts`); `IsPackable=false`; zero package refs | Pre-rail OS **logic** over vocabulary; identity = namespace + sealed class name (future Behavior identity) |
| `samples/DigitalBrain.AccountEnrichment` | Kernel OK (compiled process neuron sample) | Durable multi-module **process vocabulary** — not a composition, not Behavior install |
| `hosts/DigitalBrain.Ui` | Client + Flutter contracts | Northbound HTTP/SSE edge — **projection**, not OS logic |
| `clients/digitalbrain_*` | HTTP clients of Ui | Pixels / host chrome — never Orleans |
| `src/DigitalBrain.SourceGeneration` | Private analyzer | Compile-time capsules / dispatch — not a consumer package |

**L0 pins that already encode this honesty:**

- `CompositionBoundaryContracts` — compositions never Kernel / module runtimes / Integrations.
- `CompositionBehaviorShape` — each composition is one public sealed class; entry takes `IDigitalBrain`; no peer construction.
- `TasksContracts` (and architecture / mass-deletion) — **no `IBehavior`**.
- Hosting/testing design §15 — Behavior is a product term; Testing adds no Behavior fixture hierarchy.

**Codegraph blast (substrate used by any activation path):**

- Client authors broadcast with `IDigitalBrain.EmitAsync(Synapse)` → session `Emit` → Kernel broadcast
  catalog + owner subscriptions (`Neuron.Messaging.EmitAsync`).
- Handlers are neurons implementing `IHandle<TSynapse>` (module runtime). Pre-rail compositions are
  **pull-invoked** scripts over `IDigitalBrain`, not `IHandle` grains.
- Flutter facts (`SceneOpened`, `ControlActivated`) live in `DigitalBrain.Modules.Flutter.Contracts`
  and are journal-proven by Flutter / Ui / Compositions tests today.
- **No type named `DigitalBrainActivated` / `BootOnActivation` exists in tree** (prompt name only).

### Honesty split already ratified

| Kind | Home today | Is Behavior install? |
| --- | --- | --- |
| Framework substrate | Abstractions / Kernel / Client / Testing / generator | No |
| Module vocabulary | `Modules.*.Contracts` (+ runtime) | No |
| OS logic (shell / surfaces) | `samples/DigitalBrain.Compositions` | **No** — pre-rail stand-in |
| Process sample | `samples/DigitalBrain.AccountEnrichment` | No |
| Edge projection | Ui / Flutter host / Mcp | No |
| Install rail | **Designed, unbuilt** | N/A |

---

## 1. Grill targets

### 1.1 `DigitalBrainActivated` type (activation fact)

**Recommendation:** **Keep (Designed) → home in `DigitalBrain.Abstractions` when red BDD exists.**  
Do **not** invent a new NuGet package, do **not** put it on Flutter contracts, do **not** put it in
Client or Kernel assemblies as a special case API.

| Candidate home | Verdict | Why |
| --- | --- | --- |
| `DigitalBrain.Abstractions` | **Recommended eventual home** | Domain-neutral **lifecycle fact**, same class as `CapabilityRequested*`: framework-owned synapse vocabulary that any consumer may emit/observe without taking a module. Namespace stays leaf; wire alias stable (`db.*` pattern). Client already emits any `Synapse`. |
| `DigitalBrain.Modules.Flutter.Contracts` | **Reject** | Activation is not UI vocabulary. Forcing every activation consumer through Flutter packages inverts module ownership. Flutter **reacts** via `IShell` / `SceneOpened`; it does not own “brain activated.” |
| New `DigitalBrain.Behaviors*` / `IBehavior` package | **Reject (must-not-return)** | Architecture §5 + mass deletion: no Behavior package theater, no `IBehavior`. |
| `samples/DigitalBrain.Compositions` only | **Reject as permanent home** | Samples are not wire vocabulary shared with Kernel serializers / multi-project proofs. A fact type that journals must be a real serializable contract reachable from silo + client. |
| `DigitalBrain.Client` | **Reject** | Client is facade verbs only; no product fact zoo. |
| `DigitalBrain.Kernel` | **Reject** | Kernel must stay free of product OS semantics; activation is a fact *about* the owner session/OS boot, not neuron mechanics. |
| Host (`Ui` / AppHost / silo `Program`) | **Reject** | Host special-case boot is the dual path Behavior OS is killing. Host may **emit** the fact via `IDigitalBrain.EmitAsync` once composition exists; it must not *be* the fact type owner. |

**Shape constraints (Designed, not coded here):**

- `record … : Synapse` with stable `[Alias("db.…")]` — thin fact, no reply, no credentials/tokens.
- Payload: owner is ambient on journals/deliveries; keep fields minimal (e.g. optional correlation /
  reason / generation only if a red test demands them). Prefer empty-or-tiny over a bag.
- Emission path: `brain.EmitAsync(new DigitalBrainActivated(…))` — broadcast, not
  `SendAsync` to a named “behavior runner.”
- Consumption pre-rail: **not** a Kernel grain auto-handler for product UI. OS composition is pull or
  later installed handler; do not invent dynamic capability registration.

**Strongest counter:** “Abstractions should stay ultra-thin; activation is OS product, not
framework.”  
**Defense:** Modules own *domain* vocabulary. Activation is the domain-neutral “owner brain is live
for OS composition” fact — parallel to capability audit facts already on Abstractions. A future
Behavior rail still needs a stable leaf type every installed script and every silo can deserialize.
Until red BDD lands, **do not add the type** (no public product API without a failing product
sentence).

**Grill decision:** **Designed home = Abstractions.** **Ship gate = red→green BDD** for
activation → UI (see §1.4). Name remains provisional until synapse-vocab agents freeze alias + fields
against that red.

---

### 1.2 `BootOnActivation` / OS behavior body (pre-rail)

**Recommendation:** **Re-home as composition under `samples/DigitalBrain.Compositions`** (stay
samples; not NuGet; not installed Behavior). Body uses only `IDigitalBrain` + selected `*.Contracts`
+ approved BCL — future Behavior allowlist (architecture §5 “OS composition before the rail”).

| Candidate home | Verdict | Why |
| --- | --- | --- |
| `samples/DigitalBrain.Compositions` (e.g. `DigitalBrain.Shell` / `DigitalBrain.Os`) | **Recommended pre-rail home** | Already the honesty home for shell policy (`OpenHome`, `PostAuthBootstrap`, `NavigateShell`) and multi-module surfaces. L0/L1 graph + shape pins already protect the allowlist. One public sealed class per file = future Behavior identity. |
| `hosts/DigitalBrain.Ui` / AppHost / silo startup | **Reject** | Edge/host owns projection and principal→owner bind, not shell orchestration after activation. |
| Flutter module runtime | **Reject** | Modules own vocabulary (`IShell.Open`, `SceneOpened`), not product boot policy. |
| Kernel | **Reject** | Domain-free rule. |
| New public product package for “OS behaviors” | **Reject pre-rail** | Would invent a public Behavior surface without install rail. Compositions stay **samples**. |
| Promote compositions to NuGet now | **Reject** | `packages.md` + boundary tests: not packable; not installed Behaviors. |

**Relationship to existing compositions:**

| Existing | Role vs boot |
| --- | --- |
| `PostAuthBootstrap` / `OpenHome` | Already “open home via `IShell`” — closest stand-in for first screen after bind. Boot composition should **compose** this vocabulary path, not fork a second open-home dual. |
| `NavigateShell`, surfaces | Downstream OS logic; not the activation emitter. |
| AccountEnrichment **process** sample | Unrelated durable process neuron — do not merge into boot composition. |

**Pre-rail invocation model (honest):**

```text
Test / temporary host entry
  → (optional) Emit DigitalBrainActivated   // when type + red exist
  → pull-invoke BootOnActivation.RunAsync(IDigitalBrain, …)
  → IShell.Open(OpenScene) → journal SceneOpened
```

Until installed handlers exist, **react** means ordinary C# that is *driven by* the activation fact
in the product sentence (test emits fact, then runs composition — or composition is the sole entry
and the fact is emitted as part of the boot script). Do **not** require Kernel auto-dispatch of
sample classes.

**Strongest counter:** “Put boot logic in Flutter hosting so Desktop always opens home.”  
**Defense:** That reintroduces host dual paths and AppHost product logic. Architecture 4.6 + packages
row: compositions own logic; hosting projects surfaces.

**Grill decision:** **Keep compositions as samples pre-rail.** Add boot body only behind red BDD;
prefer extending/clarifying `PostAuthBootstrap` / `OpenHome` over a second parallel home-open path
unless the product sentence needs a distinct Behavior identity for “react to activation.”

---

### 1.3 Future install rail (Designed only — where would it live?)

**Status:** Designed, unbuilt. Architecture §5; hosting design §15; ownership Hold #8.  
**Must not invent:** `IBehavior`, `IBehaviorTest`, Behavior runner NuGet, public “install Behavior”
API theater, or Kernel domain knowledge.

| Rail concern | Designed package home | Explicit non-homes |
| --- | --- | --- |
| Proposal / approval / install / rollback **facts** (journaled, reversible) | Prefer **`DigitalBrain.Abstractions`** leaf records (domain-neutral process facts) *or* a future single thin contracts package **only if** Abstractions bloat is proven — still **not** a module family named Behaviors | Not Flutter/Google/AI contracts; not samples; not Client method zoo |
| Human approval UX / edge | Application edge (Ui or dedicated edge later) binds principal; **never** “Behavior authenticates” | Not Kernel; not compositions as IdP |
| Compiler allowlist (contracts-only) | Private tooling / `SourceGeneration` era hooks when rail builds — **not** a public consumer package | Not public `IBehavior` SDK |
| Load / activate installed script | **Kernel-internal** mechanics (broadcast catalog / subscription / journaled install state) — opaque, no public runner type | Not host `Program` switch; not dynamic `IGrainFactory` capabilities |
| Author programming model | **Still `IDigitalBrain` + contracts** — same file as script and as installed behavior | No second client facade |
| Testing proofs | Ordinary **`TestBrain` journals** + optional later Testing helpers — **no** `IBehaviorTest` | Not Simulations / ModuleDriver resurrection |

**Install path (Designed narrative only):**

```text
Human proposal (journaled fact)
  → human approval (journaled, reversible)
  → install revision (identity = namespace + class name)
  → runtime: activated by existing typed synapses (external), never by name dispatch
  → body may Get/Send/Emit existing vocabulary only
```

**Pre-rail implication:** compositions **preview** the allowlist and identity shape; they are **not**
on the install rail and must not be described as Built Behaviors.

**Strongest counter:** “Ship `DigitalBrain.Behaviors` NuGet now so authors have a home.”  
**Defense:** Without proposal/approval/journaled install, a Behaviors package is theater and
violates must-not-return. Ordinary C# in samples + contracts is the honest pre-rail surface.

**Grill decision:** **Designed placement table above.** No new packages in this wave. Rail work is a
later phase; this file only freezes *where* pieces would land so package-graph agents do not invent
`IBehavior*`.

---

### 1.4 BDD tests for activation → UI

**Product sentence (north-star, from Behavior OS prompt):**

```gherkin
Given DigitalBrain is activated for an owner
When the activation synapse is committed
Then OS logic reacts (pre-rail: composition)
And UI starts through Flutter vocabulary
And the first screen is presented (journal: SceneOpened for login/home — design chooses key)
```

**Evidence oracles:** typed journals (`DigitalBrainActivated` when present, `SceneOpened`); optional
Ui HTTP/SSE / Dart projection only if the sentence claims the edge — never “it compiled.”

| Proof layer | Package / project | Role |
| --- | --- | --- |
| **Primary L1 product sentence** | `tests/DigitalBrain.Compositions.Tests` | Owns activation→composition→`SceneOpened` chain over Flutter (+ modules only if multi-module boot). Matches existing `ShellAndSurfaceCompositions` style. |
| L0 shape / graph | `tests/DigitalBrain.Tests` (`CompositionBoundaryContracts`, package pins) | Keep compositions client+contracts; pin absence of `IBehavior`; when activation type lands, pin package home + alias stability. |
| Flutter vocabulary only | `DigitalBrain.Flutter.Tests` | Proves `IShell` / `SceneOpened` — **not** OS boot policy. Do not park boot BDD here. |
| Ui edge | `DigitalBrain.Ui.Tests` | Only if sentence includes HTTP/SSE presentation; edge projects journals, does not own boot logic. |
| Testing package | `DigitalBrain.Testing` | **May gain helpers later** (e.g. arrange emit+wait) — **not** a Behavior fixture hierarchy; no `IBehaviorTest`. Prefer reusing `TestBrain.Client` + `Neuron<>.Outgoing` first. |
| Live Aspire | product AppHost L2 | Residual until product OS topology Healthy is deliberately claimed — not required to place package homes. |

**Red-first rule:**

1. Author the failing composition (or Explicit red) that asserts: after activation fact commit, home
   (or designed first scene) `SceneOpened` is journaled.
2. Only then add `DigitalBrainActivated` to Abstractions and boot composition body.
3. **No new public product API without that red.**

**Strongest counter:** “Put activation BDD in Flutter.Tests because UI is Flutter.”  
**Defense:** Flutter.Tests prove module vocabulary. Boot is OS logic (compositions). Splitting keeps
modules free of product policy and matches packages.md family split.

**Grill decision:** **Compositions.Tests owns activation→UI BDD.** Testing helpers optional later.
No new public Testing Behavior API.

---

## 2. Recommendations table

| # | Artifact | Layer | Status | Package / tree home | Action | Public product API now? | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | `DigitalBrainActivated` (`Synapse` fact) | Framework vocabulary (domain-neutral) | Designed | **`DigitalBrain.Abstractions`** | Add only after red BDD | **No** until red→green | Not Flutter.Contracts; not samples; not Client/Kernel product types |
| 2 | Emit activation | Client programming model | Built substrate | **`IDigitalBrain.EmitAsync`** (existing) | Reuse; no new emit API | Already Built | Host/test emits; no name-dispatched runner |
| 3 | `BootOnActivation` / boot body | OS logic (pre-rail) | Designed / partial via `OpenHome`/`PostAuthBootstrap` | **`samples/DigitalBrain.Compositions`** | Stay samples; sealed class identity | **No** NuGet; sample only | Prefer compose existing shell opens; avoid dual home-open paths |
| 4 | First screen vocabulary | Module vocabulary | Built | **`DigitalBrain.Modules.Flutter.Contracts`** (`IShell`, `OpenScene`, `SceneOpened`) | Consume only | Already Built | UI is behavior *over* vocabulary, not widgets in C# |
| 5 | Install rail (proposal→approve→install→rollback) | Product rail | Designed unbuilt | Facts → Abstractions (or later thin leaf); load → Kernel-internal; author → Client | Design-only; **no packages invented this wave** | **No** | **Must not invent `IBehavior*` packages** |
| 6 | Behavior identity | OS | Designed shape pre-rail | namespace + sealed class (compositions preview) | Keep one class / file | Sample shape only | Same identity rule as future install |
| 7 | Activation→UI BDD | Test | To author red-first | **`DigitalBrain.Compositions.Tests`** primary | Red BDD before type/body | N/A | Journals oracles; Explicit if unfinished |
| 8 | L0 graph honesty | Test | Built | **`DigitalBrain.Tests` boundary/package pins** | Keep; extend when type lands | N/A | Compositions non-packable; no Kernel on compositions |
| 9 | Testing helpers | Test harness | Optional later | **`DigitalBrain.Testing`** | Helpers only if duplication hurts | No Behavior test interface | Hold hosting design §15 |
| 10 | Ui / Flutter host | Edge | Built (projection) / live residual | `hosts/DigitalBrain.Ui`, `clients/*`, Flutter.Aspire.Hosting | Project facts only | Edge APIs only | Never own boot policy |
| 11 | Compositions NuGet promotion | Packaging | Rejected pre-rail | samples only | **Stay samples** | **No** | packages.md row “not installed Behaviors” |
| 12 | AccountEnrichment | Sample process neuron | Built sample | `samples/DigitalBrain.AccountEnrichment` | Keep separate from compositions | Sample only | Not boot; not Behavior install |

---

## 3. Recommendation form (grill)

### R1 — Activation fact home = Abstractions

| Field | Content |
| --- | --- |
| **Recommendation** | Place `DigitalBrainActivated` in `DigitalBrain.Abstractions` when BDD demands it; ship only behind red. |
| **Strongest argument against** | Pollutes the leaf with OS product semantics; maybe wait for a Behaviors contracts package. |
| **Defense or fold** | **Defend:** domain-neutral lifecycle facts already live on Abstractions; a Behaviors package without rail is theater; Flutter home is wrong ownership. |
| **Evidence** | `CapabilityRequested` in Abstractions; `SceneOpened` in Flutter.Contracts; architecture modules-own-domain; mass-deletion forbids Behavior API theater; no activation type in tree today. |
| **Consumer today** | Behavior OS campaign + future BDD only — **no production silo consumer yet** → do not add type early. |

### R2 — Pre-rail boot body = compositions samples

| Field | Content |
| --- | --- |
| **Recommendation** | Keep boot/OS reaction body in `samples/DigitalBrain.Compositions`; do not promote to NuGet; do not host-wire as product Behavior. |
| **Strongest argument against** | Samples are invisible to product AppHost; users never get boot until rail. |
| **Defense or fold** | **Defend (honest pre-rail):** architecture §5 already states compositions are pull-invoked by tests, not installed. Product AppHost OS Healthy is residual separately. Faking install is worse than sample honesty. |
| **Evidence** | `packages.md` compositions row; `CompositionBoundaryContracts`; `OpenHome` / `PostAuthBootstrap` L1 journals. |

### R3 — Install rail homes without `IBehavior`

| Field | Content |
| --- | --- |
| **Recommendation** | Designed: journaled rail facts on Abstractions (or later thin leaf); Kernel-internal load; Client remains author API; Testing stays fixture-only. |
| **Strongest argument against** | Authors need a discoverable `DigitalBrain.Behaviors` package. |
| **Defense or fold** | **Defend:** discoverability without approval rail trains the wrong model. Identity is namespace+class over contracts, not a marker interface package. |
| **Evidence** | architecture §5; hosting design §15; TasksContracts null `IBehavior`; ownership Hold #8. |

### R4 — BDD home = Compositions.Tests

| Field | Content |
| --- | --- |
| **Recommendation** | Primary activation→UI BDD in `DigitalBrain.Compositions.Tests`; Testing helpers later only if needed. |
| **Strongest argument against** | Prefer Flutter.Tests or Ui.Tests as “UI.” |
| **Defense or fold** | **Defend:** vocabulary vs logic split; edge is projection. |
| **Evidence** | `ShellAndSurfaceCompositions` already journals `SceneOpened` from compositions; Flutter.Tests are vocabulary round-trips. |

---

## 4. Thirteen grill answers (package graph mission)

1. **What is the product sentence?**  
   When an owner’s DigitalBrain activates, a typed activation synapse is committed; OS logic reacts and
   starts UI via Flutter vocabulary so the first screen is journaled (`SceneOpened`) — not via host
   chrome special cases.

2. **Is it framework vocabulary, module neuron, OS behavior, edge, or test witness?**  
   Activation fact = **framework vocabulary** (Abstractions). Boot body = **OS behavior** (pre-rail
   composition). UI open = **module vocabulary**. Proof = **test witness** on journals. Edge =
   optional projection only.

3. **Belongs in proposed home? If not: delete / move / internalize / re-home as behavior?**  
   Yes for the table in §2. Reject Behaviors NuGet, Flutter-owned activation, host boot policy.

4. **Aligns with framework = neurons+synapses, OS = behaviors?**  
   Yes: activation is a synapse; reaction is behavior/composition over neurons; no name dispatch.

5. **Consumer today?**  
   Campaign design + future BDD. No production install rail consumer. Compositions tests will be the
   first real consumer of the chain.

6. **Built vs Designed honesty?**  
   Substrate (Client emit, Flutter vocab, compositions samples, Testing journals) = **Built**.
   Activation type, install rail, auto-reaction of installed scripts = **Designed**. Do not label
   compositions as installed Behaviors.

7. **New public product API?**  
   **None without red BDD.** Prefer reusing `EmitAsync` + existing `IShell` path.

8. **Package graph blast?**  
   Abstractions leaf → Client/Kernel already depend. Compositions already reference Abstractions.
   Adding a synapse record there does not force module runtimes onto compositions. Putting it on
   Flutter.Contracts would expand compositions’ semantic coupling incorrectly (activation ≠ UI).

9. **Dual path risk?**  
   Host `Program` / Ui open-home vs composition open-home; AppHost “always open home”; second
   home-open composition that ignores `OpenHome` constants. **Mitigation:** one boot identity;
   reuse scene key/title constants; edge only projects.

10. **Must-not-return?**  
    `IBehavior` / `IBehaviorTest` / Behavior runner package / ProbeHost / Simulations / `IFlutter` god
    / Behavior APIs without rail — all forbidden. This grill does not reintroduce them.

11. **Delete / simplify opportunities?**  
    Pre-rail: prefer one boot composition over `PostAuthBootstrap` + `OpenHome` duplication *if*
    product sentence collapses them — only after BDD shows both are the same Behavior identity.
    Do not delete process sample or Flutter vocabulary to “make room” for Behaviors.

12. **Test oracle for the claim?**  
    Compositions L1 journals; L0 composition boundary; package inventory pins. Root gate remains
    full `dotnet test DigitalBrain.slnx` when implementation ships — this file is design-only.

13. **What did we not invent?**  
    No new packages, no `IBehavior*`, no Kernel boot hooks, no code under `src/` or `samples/`.
    Only this durable placement grill.

---

## 5. Wave handoff (non-binding sequence)

| Order | Work | Mission flavor | Depends on |
| --- | --- | --- | --- |
| A | Freeze activation fact name/fields/alias against red | `synapse-vocab` + `bdd-red` | This grill’s Abstractions home |
| B | Red activation→`SceneOpened` in Compositions.Tests | `bdd-red` | A or Explicit hold with failing proof |
| C | Boot composition body (samples) | `behavior-impl` pre-rail | B red |
| D | Green + L0 pin for type home / compositions still non-packable | `test-contract` | C |
| E | Install rail design deep-dive (still Designed) | `rail-proposal` | Architecture §5; not blocked on A–D for honesty docs |
| F | Testing helpers only if proofs hurt | `test-contract` | Real duplication |

---

## 6. Explicit non-goals (this document)

- Implementing activation, boot, or rail code.
- Promoting compositions to NuGet.
- Claiming product AppHost OS-surface Healthy / live Aspire as Built.
- Calendar Time / `IReminder` / supervised `IWorker`.
- Product journal observation on `IDigitalBrain` (still Designed).
- Merging AccountEnrichment process sample into compositions.

---

## 7. One-line summary

**Activation fact → Abstractions (behind red); boot OS body → compositions samples; install rail →
Designed Kernel-internal + Abstractions facts + Client author model with zero `IBehavior` packages;
activation→UI BDD → Compositions.Tests; Testing may grow helpers later — never a Behavior test
framework.**
