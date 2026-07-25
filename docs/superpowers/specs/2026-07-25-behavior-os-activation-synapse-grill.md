# Activation synapse placement grill (Wave B0 · agent 3 · synapse-vocab)

**Date:** 2026-07-25  
**HEAD at grill:** `7ffaa21a415ed676ea4735cab06fa2de29a2b4d4`  
**Write scope:** residual grill only — `docs/superpowers/specs/2026-07-25-behavior-os-design.md` did **not** exist at start, so that design file was **not** created or edited.

**Vision (one sentence):** Framework ships neurons+synapses; OS is behaviors (including UI); activation is a broadcast synapse OS/Flutter-facing behavior reacts to — never a host special-case or name-dispatched rail.

**Mission type:** `synapse-vocab`  
**Scoring applied:** §§1 (framework purity), 6 (architecture / module vocabulary), 11 (grill honesty), 12 (codegraph-first).

---

## Codegraph-first (required paste)

```
Codegraph query: Synapse record base class existing synapse examples in Abstractions and Flutter.Contracts GenerateSerializer Alias
What it does (1 sentence): Maps the abstract Synapse base, framework Capability* facts, Flutter first-five SceneOpened/ControlActivated facts, and journal/edge projection dependents.
Callers / consumers:
  - CapabilityRequested / CapabilityCompleted → Kernel Neuron.Capability (+ Delegation)
  - SceneOpened → ShellNeuron emit; ShellEventFeed SSE project; Flutter/UI tests
  - ControlActivated → UiEndpoints / SceneNeuron; Flutter tests
Dependents / blast radius:
  - ObservedSynapse / TestJournal watch paths (Testing)
  - Flutter.Contracts first-five pin (FlutterContracts tests + flutter-wire-contracts.golden.json)
  - Ui edge SSE (hosts/DigitalBrain.Ui) projects SceneOpened only — not a generic OS bus
Dual paths (if any): none for an activation fact (type does not exist yet)
Public vs internal: all product synapses above are public sealed records with [GenerateSerializer]+[Alias]
Framework vs OS vs edge:
  - Synapse base + Capability* = framework Abstractions
  - SceneOpened / ControlActivated / OpenScene / IShell / IScene = module Flutter.Contracts
  - SSE/HTTP = edge projection of Flutter vocabulary, not vocabulary ownership
```

Secondary explore (Capability* + SceneOpened + ControlActivated):

```
Blast radius highlights:
  CapabilityCompleted → Neuron.Capability.cs, Neuron.Capability.Delegation.cs
  CapabilityRequested → Neuron.Capability.cs
  ControlActivated → UiEndpoints, SceneNeuron + Flutter tests
  SceneOpened → ShellNeuron + FlutterVocabulary tests
  ICompiledModule.Activate → silo module catalog activation (compile/host substrate — not product OS boot fact)
```

---

## Recommendation form

```
Recommendation: invent synapse DigitalBrainActivated in DigitalBrain.Abstractions
  (candidate A). Do not put it in Flutter.Contracts; do not invent an OS package yet;
  do not home vocabulary in Compositions.

Strongest argument against:
  Architecture “Modules own vocabulary” + Abstractions growth — activation is a product OS
  sentence, not Kernel request reification like CapabilityRequested; stuffing it into
  Abstractions risks framework domain creep.

Defense / fold:
  Defend A for the first vertical. Activation is owner-scoped programming-model readiness
  (“brain activated for this owner”), not Gmail/CRM/UI chrome. Behavior allowlist always
  includes Abstractions; OS behaviors must react without a Flutter module dependency.
  Flutter.Contracts is pinned first-five (tests + golden) and owns screen vocabulary only.
  Compositions are logic-over-vocabulary and must not mint durable synapse types
  (architecture §5). A new OS.Contracts package for a single type with no neuron, no
  runtime, no Aspire.Hosting is package theater — fold B until OS lifecycle vocabulary
  grows past one fact (then re-home the family). Capability* already prove Abstractions
  holds non-domain brain facts with db.* aliases; activation is the same tier, not UI.

Evidence (codegraph | tree | architecture):
  - codegraph blast radius above
  - src/DigitalBrain.Abstractions/{Synapse,Capability*}.cs — framework facts
  - modules/DigitalBrain.Modules.Flutter.Contracts/* — first-five only; aliases flutter.*
  - tests/.../FlutterContracts.cs — vocabulary pin forbids sixth public type
  - docs/architecture.md §2 (Capability* causal facts), §3 (modules own vocabulary),
    §4.6 Flutter first vertical, §5 Behaviors (activated by existing typed synapses;
    compositions compose existing vocabulary only)
  - SynapseDelivery already carries Timestamp, SynapseId, CorrelationId, Caller
```

---

## Candidate grill

| Candidate | Home | Verdict |
| --- | --- | --- |
| **A** | `DigitalBrain.Abstractions` | **Recommend** for first vertical |
| **B** | New OS contracts package (e.g. `DigitalBrain.Modules.OS.Contracts` / namespace `DigitalBrain.OS`) | **Hold** — correct *if* OS fact family grows; premature for one type |
| **C** | `DigitalBrain.Modules.Flutter.Contracts` | **Reject** — UI vocabulary ≠ brain activation; breaks first-five; forces non-UI OS behaviors onto Flutter |
| **D** | Compositions only (sample types, not real shared synapse) | **Reject** — not product vocabulary; compositions must not invent durable synapse types; handlers outside samples cannot depend on samples |

### A — Abstractions (chosen)

- **For:** Universal boot fact; always on behavior allowlist; matches `db.*` lifecycle/causal tier with Capability*; no Flutter coupling; no empty module package; `IDigitalBrain.EmitAsync` can broadcast any `Synapse` known to the silo serializer once the type ships.
- **Against:** Module-vocabulary rule; Abstractions surface growth; not Kernel reification of a method call.
- **Fold condition:** If design later adds SignedIn / SignedOut / SessionEnded / ProfileSwitched as a family, **move the family** to an OS/Session Contracts package (B) in one deliberate re-home — do not leave a mixed home.

### B — New OS contracts package

- **For:** Strict reading of “modules own vocabulary”; clean namespace `DigitalBrain.OS`; keeps Abstractions leaf pure of product OS words.
- **Against:** Invents Contracts (+ residual package-graph pins, packages.md row, catalog story) with **no** runtime neuron and **no** second type; Behavior rail still Designed; violates delete/simplify for a single record.
- **When to promote:** Second durable OS lifecycle fact, or a real OS module runtime that emits/handles activation.

### C — Flutter.Contracts

- **Reject hard.** First-five public surface is pinned (`IShell`, `IScene`, `OpenScene`, `SceneOpened`, `ControlActivated`). Activation is not a scene/shell wire type. Vision “Flutter reacts” means an **OS behavior** uses Flutter vocabulary *after* the fact, not that the fact is Flutter-owned. Must not invent `IFlutter`. Must not put widgets in C#.

### D — Compositions only

- **Reject hard.** `samples/DigitalBrain.Compositions` is pre-rail **logic** (`PostAuthBootstrap` already opens home via `IShell` without an activation fact). Architecture §5: compositions compose existing vocabulary; they do not mint shared durable types. A composition-local record is invisible to module handlers and future installed behaviors.

---

## Chosen shape (not implemented this cycle — design grill only)

```csharp
namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.digitalbrain-activated")]
public sealed record DigitalBrainActivated(
    [property: Id(0)] OwnerId Owner) : Synapse;
```

### Alias

| Item | Value |
| --- | --- |
| Type name | `DigitalBrainActivated` |
| Alias | **`db.digitalbrain-activated`** |
| Convention match | Framework facts use `db.<kebab>` (`db.capability-requested`, `db.capability-completed`, `db.owner-id`, …). Module facts use `<module>.<kebab>` (`flutter.scene-opened`, `time.countdown-elapsed`). Sample process uses `db.account-enrichment.*` under a sample namespace. Activation is framework-tier boot → **`db.`** prefix, not `flutter.` |

### Fields grill

| Field | Keep? | Rationale |
| --- | --- | --- |
| **OwnerId Owner** | **Yes** | Product sentence is “activated **for an owner**.” Explicit body field lets BDD assert `activated.Synapse.Owner` without decoding `SynapseDelivery.Caller`. Emitter should pass `brain.Owner` so payload matches ambient client owner. Mismatch is journal-visible bug signal. |
| **ShellName** | **No** | Couples brain activation to Flutter host env (`DIGITALBRAIN_SHELL` / default `"desk"`). Shell selection is composition/behavior/`IShell` concern after activation, not part of the boot fact. |
| **CommandId** | **No** | Activation is a lifecycle **broadcast fact**, not a retryable domain command. Identity lives on `SynapseDelivery.SynapseId` / `CorrelationId`. When a behavior opens UI it mints a fresh `CommandId` on `OpenScene` (existing pattern). |
| **Timestamp** | **No** | Already on `SynapseDelivery.Timestamp`. No product synapse in tree duplicates envelope time on the record body (Capability*, SceneOpened, ControlActivated, CountdownElapsed pattern). |

### Emitter / reactor (designed, not Built rail)

```text
IDigitalBrain (owner-scoped)
  → EmitAsync(new DigitalBrainActivated(Owner))   // composition entry or future host boot — broadcast fact
  → OS behavior IHandle<DigitalBrainActivated>    // pre-rail: composition class; later: approved behavior
  → IShell.Open(OpenScene(...))                   // existing Flutter vocabulary
  → SceneOpened journal + Ui SSE projection       // Built edge path
```

Honesty: Behavior **proposal/install/approval/rollback rail remains Designed** — do not claim Built. Pre-rail compositions may **react** and pull-invoke the same way `PostAuthBootstrap` does today; they are not installed Behaviors.

---

## Architecture §6 (registry) assessment

§6: generated catalog entries derive from public namespace, contract type name, handled/emitted synapse types, owning module. Nothing registers at runtime.

| Check | Result |
| --- | --- |
| Activation as Abstractions public type | Enters compile-time serializer/dispatch surface with other Abstractions synapses; not a runtime registry invent |
| No dynamic capability | Compliant — typed record only |
| Natural-language path later | Type name `DigitalBrainActivated` + alias is stable identity; no enum/descriptor table |
| Module catalog | No new module required for A; B would add a Contracts package to the catalog story later |

§6 does **not** force a module package for every synapse; Capability* already live outside modules.

---

## Grill board (13)

1. **What does this thing do?** Broadcast fact that this owner’s DigitalBrain session is activated so OS behaviors may start (including first UI via Flutter vocabulary).
2. **Framework / module / OS behavior / edge / test?** **Framework vocabulary** (Abstractions synapse). Reactors are OS behaviors/compositions; Flutter edge projects **downstream** `SceneOpened`, not activation itself.
3. **Consumer today?** **None in code** — type unbuilt. Designed consumers: OS behavior / composition (`IHandle` or pull-after-emit), BDD journal assert, later optional edge only if product requires SSE of activation (not required for first vertical; SceneOpened already proves UI).
4. **architecture.md placement?** Deliberate extension of §2/§5: not listed by name today. §3 modules-own-vocabulary is the counter; §5 “activated by existing typed synapses” + Abstractions always-allowed is the defense. Not a silent Built claim.
5. **UI path:** Flutter reacts to activation **indirectly**: behavior handles `DigitalBrainActivated` → `IShell.Open` → journals `SceneOpened` → Ui SSE. Screen vocabulary remains first-five only.
6. **If deleted?** Nothing breaks today (absent). Once shipped, OS boot sentence and activation→UI BDD lose their typed cause.
7. **Invent Behavior install rail?** **No** — fact type only; no `IBehavior`, no name dispatch, no approval API.
8. **Kernel learns a domain word?** **No Kernel change required** for the type home. Emitter uses existing `EmitAsync`. Kernel must not grow login/scene knowledge when implementing emit site.
9. **Proof shape?** Future BDD + journal (`DigitalBrainActivated` then `SceneOpened`); not compile-only. **This cycle does not claim green** — no build/test command run for implementation (docs-only).
10. **Claimed without command?** Gate green **not claimed**. Tree presence of design.md checked via directory listing; HEAD via `git rev-parse`.
11. **Foreign dirty tree?** `git status --porcelain` empty at start after HEAD record.
12. **One layer in/out?** In = Kernel-only private signal (wrong — not journaled product vocabulary). Out = Flutter.Contracts or compositions (rejected). B is the correct *out* if the OS fact family grows.
13. **New engineer home?** Yes: read vision (activation synapse) + §5 (behaviors react to typed synapses; compositions don’t mint vocabulary) + Abstractions Capability* precedents + Flutter first-five pin → Abstractions for the single boot fact; OS package only when lifecycle vocabulary becomes a family.

---

## Scoring gate (this cycle)

| § | Applied? | Note |
| --- | --- | --- |
| 1 Framework purity | Yes | Fact is substrate/boot vocabulary, not UI logic or widgets |
| 6 Architecture alignment | Yes | Module rule grilled; A defended with fold-to-B promotion path; no Built/Designed lie |
| 11 Grill honesty | Yes | Full form + 13 answers; counter stated |
| 12 Codegraph-first | Yes | Explore before write; blast radius pasted |

Forbidden avoided: no `IFlutter`; no widgets in C#; no Behavior rail Built claim; no code invent this cycle beyond this residual doc.

---

## Diff-grill (docs only)

1. **What did I add that has no consumer today?** This residual grill doc — and a **recommended** type that is not yet in the tree. Consumer is future B0/B1 design merge + implementation waves.
2. **What did I claim without running a command?** Did not claim build/test green. Verified design.md absence by listing `docs/superpowers/specs/`; HEAD via git.
3. **What changed that I did not change?** Nothing observed foreign at start (clean porcelain).

---

## Merge note for design.md author (peer)

If/when `2026-07-25-behavior-os-design.md` appears, fold this residual into section **## Activation synapse placement** without re-grilling from scratch:

- **Home:** `DigitalBrain.Abstractions` (promote family → OS.Contracts later)
- **Type / alias:** `DigitalBrainActivated` / `db.digitalbrain-activated`
- **Fields:** `OwnerId Owner` only
- **Reject:** ShellName, CommandId, Timestamp on payload; Flutter.Contracts home; compositions-only type
