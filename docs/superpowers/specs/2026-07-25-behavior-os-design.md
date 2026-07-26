# Behavior OS design — activation → shell home

> **Post-rail execution model superseded:** This document remains the historical record of the
> current compiled pre-rail activation path. The approved owner-scoped `BehaviorNeuron` plus
> single-file program model is defined in
> [`2026-07-26-behavior-operating-system-runtime-design.md`](2026-07-26-behavior-operating-system-runtime-design.md).
> Where the documents conflict about future Behavior identity, installation, or execution, the
> 2026-07-26 design wins.

**Date:** 2026-07-25  
**Status:** Design lock + **L1 pre-rail Built green** (Wave B7 docs-honesty). Product chain journals-proven; **not** install rail; **not** auto `IHandle` reaction; **not** product AppHost OS Healthy.  
**Vision:** Framework = neurons + synapses; OS = behaviors (including UI); activation is a synapse Flutter reacts to **via projection of shell facts**, not by consuming activation.

**Authority order:** CLAUDE.md · architecture §§1–3, §4.6 Flutter, §5 Behaviors · packages.md · hosting/testing design 2026-07-24 · ownership Holds #6/#7/#8/#14.

**Residual grills (historical B0 locks; this file wins on conflict; scorecard tracks live residual truth):**

| Residual | Mission lock folded here |
| --- | --- |
| `2026-07-25-behavior-os-activation-synapse-grill.md` | Fact home, alias, fields |
| `2026-07-25-behavior-os-emitter-grill.md` | Who emits |
| `2026-07-25-behavior-os-flutter-reaction-grill.md` | Flutter path + first screen |
| `2026-07-25-behavior-os-package-graph-grill.md` | Package homes table |

**Codegraph (B7 docs-honesty):** query `DigitalBrainActivated BootOnActivation BehaviorOsActivationBoot`.

| Finding | Evidence |
| --- | --- |
| `DigitalBrainActivated` type | **Built** — `src/DigitalBrain.Abstractions/DigitalBrainActivated.cs` (`db.digitalbrain-activated`, `Owner` only) |
| `ActivateDigitalBrain` | **Built** pre-rail sample — `samples/DigitalBrain.Compositions/Shell/ActivateDigitalBrain.cs` → `EmitAsync` |
| `BootOnActivation` | **Built** pre-rail sample — `samples/DigitalBrain.Compositions/Shell/BootOnActivation.cs` → `OpenHome` |
| `OpenHome` | Built sample — `SceneKey = "home"`, `SceneTitle = "Home"` |
| `IDigitalBrain.EmitAsync` | Built broadcast pipe → session gateway |
| `Connect` | Pure construction — no journal |
| `SceneOpened` | Built Flutter vocab; ShellNeuron emits; Ui `ShellEventFeed` projects **only** `SceneOpened` to SSE |
| Auto `IHandle<DigitalBrainActivated>` reaction | **Designed residual** — pre-rail is **pull** `RunAsync`, not handler auto-wire |
| Install rail | **Designed unbuilt** |
| Module `ICompiledModule.Activate` | Silo DI registration — not owner OS boot |

HEAD at B7 honesty fold: `7ffaa21a415ed676ea4735cab06fa2de29a2b4d4` (re-check before stage).

---

## LOCKED decisions (single source of truth)

Do **not** invent new product API beyond these locks. Residual grills remain historical; **if residual and this section conflict, this section wins.**

### Built vs Designed (honesty board)

| Artifact | Status | Notes |
| --- | --- | --- |
| `DigitalBrainActivated` | **Built** (framework vocabulary) | Abstractions; Owner only; alias pinned |
| `ActivateDigitalBrain` | **Built** pre-rail L1 sample | Pull-invoked emitter — **not** installed Behavior |
| `BootOnActivation` | **Built** pre-rail L1 sample | Pull-invoked reactor → `OpenHome` — **not** `IHandle` auto |
| Activation product sentence (journals) | **L1 green** | See test names below |
| Auto `IHandle<DigitalBrainActivated>` reaction | **Designed residual** | Post-rail / future; not claimed Built |
| Install / approval / rollback rail | **Designed unbuilt** | No `IBehavior*` theater |
| Flutter consumes activation | **No** — **`SceneOpened` only** | SSE `scene-opened`; unchanged |
| Product AppHost OS Healthy / live topology | Residual | Not L1 claim |

### L1 product sentence — green proofs

Pre-rail pull chain is journal-proven in `DigitalBrain.Compositions.Tests`:

| Class | Method | DisplayName (product sentence) |
| --- | --- | --- |
| `BehaviorOsActivationBoot` | `ActivationSynapseDrivesOsBehaviorToStartUi` | Given DigitalBrain is activated for an owner; When DigitalBrainActivated is committed; Then an OS behavior/composition reacts and the UI starts via IShell |
| `BehaviorOsActivationBoot` | `ActivationCommittedObservesSceneOpenedHome` | When DigitalBrainActivated is committed, SceneOpened for home first screen is presented (journal evidence) |
| `BehaviorOsActivationHonesty` | `ActivationSynapseDrivesBootNotHostProgram` | activation boot is owned by ActivateDigitalBrain + BootOnActivation compositions — not host Program |
| `BehaviorOsActivationHonesty` | `NoBehaviorByNameDispatchApi` | no Behavior-by-name dispatch API — IBehavior absent is success |

Both Boot facts are **default green** (not Explicit). Honesty residual dual `PostAuthBootstrap`/`OpenHome` path stays Explicit hold.

```text
ActivateDigitalBrain.RunAsync(brain)
  → EmitAsync(DigitalBrainActivated(Owner))
  → BootOnActivation.RunAsync(brain, shellName, …)   // pre-rail pull; post-rail Designed: typed IHandle
    → OpenHome.RunAsync / IShell.Open(OpenScene home)
      → ShellNeuron EmitAsync(SceneOpened)
```

### L1 — Activation fact

| Item | Lock |
| --- | --- |
| Type | `DigitalBrainActivated` |
| Home | `DigitalBrain.Abstractions` |
| Wire alias | `[Alias("db.digitalbrain-activated")]` |
| Base | `: Synapse` |
| Fields | **`OwnerId Owner` only** (`[property: Id(0)]`) |
| Status | **Built** |
| Reject on payload | `ShellName`, `CommandId`, `Timestamp` (envelope owns time/id/correlation) |
| Reject homes | Flutter.Contracts; compositions-only type; Client/Kernel product types; new OS.Contracts package for a single type |
| Promote later | If OS lifecycle vocabulary becomes a family (SignedIn / SessionEnded / …), re-home the **family** to a thin OS contracts package in one deliberate move |

```csharp
// Built — src/DigitalBrain.Abstractions/DigitalBrainActivated.cs
namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.digitalbrain-activated")]
public sealed record DigitalBrainActivated(
    [property: Id(0)] OwnerId Owner) : Synapse;
```

Alias convention: Abstractions substrate uses `db.<kebab>`; Flutter uses `flutter.<kebab>`; modules use `<module>.<kebab>`. Activation is substrate identity → **`db.`**, not `flutter.*`.

### L2 — Emitter (pre-rail)

| Item | Lock |
| --- | --- |
| Emitter | Pre-rail composition **`ActivateDigitalBrain`** → `IDigitalBrain.EmitAsync(new DigitalBrainActivated(…))` |
| Status | **Built** sample (pull-invoked) |
| Pipe | Existing Client `EmitAsync` only — **no new client verbs** |
| Reject emitters | `Connect` / session first-touch auto-emit; Ui HTTP bind emit; AppHost / host `Program.cs`; module capsule `ICompiledModule.Activate` |
| Invoker pre-rail | Tests + deliberate ordinary C# holding `IDigitalBrain`; never silo module Activate; never AppHost product rules |
| Post-rail | Same fact type; install rail may auto-wire handlers — still **broadcast fact**, never name dispatch |

### L3 — Reactor (pre-rail)

| Item | Lock |
| --- | --- |
| Reactor | Pre-rail composition **`BootOnActivation`** |
| Status | **Built** sample (pull-invoked `RunAsync`) |
| Action | `IShell.Open` home using **`OpenHome.SceneKey` / `OpenHome.SceneTitle`** (`"home"` / `"Home"`) |
| Prefer | Compose **`OpenHome`** rather than duplicate scene constants |
| Not | `RunBehavior("BootOnActivation")` string dispatch; host `main` special-case without the activation fact |
| Separation | Emitter (`ActivateDigitalBrain`) and reactor (`BootOnActivation`) stay **separate** product sentences — do not collapse into one god composition long-term |
| Auto `IHandle` reaction | **Designed residual** — not Built L1 |

### L4 — Flutter reaction

| Item | Lock |
| --- | --- |
| Flutter consumes | **`SceneOpened` only** (SSE event `scene-opened`) — **unchanged; still Built path only** |
| Flutter does **not** | Know or subscribe to `DigitalBrainActivated` |
| Path | OS open → journal `SceneOpened` → Ui `ShellEventFeed` SSE → Dart `watchShellEvents` → `ShellSurfaceController` / chrome |
| Edge POST open-scene | **Keep** as northbound mutator for tests/host tooling — **not** the product activation reaction home |
| Optional later | Project activation on SSE only with a real consumer + red proof |

### L5 — First screen

| Item | Lock |
| --- | --- |
| First product OS screen | **Shell home** — `OpenHome.SceneKey = "home"`, `OpenHome.SceneTitle = "Home"` |
| Not first OS screen | IdP login chrome as a Behavior-owned scene |
| Auth ownership | Credentials / IdP / cookies / tokens = **edge only**; never in journals; never “Behavior authenticates” |
| Post-auth phrasing | Edge binds principal → `OwnerId`; composition/Behavior orchestrates shell UX (**post-auth composition**) |

### L6 — Install rail

| Item | Lock |
| --- | --- |
| Status | **Designed, unbuilt** |
| Forbidden theater | `IBehavior`, `IBehaviorTest`, Behavior-by-name dispatch, public install API without human-approved journaled proposal |
| Until rail | Changes ship via source control + rebuild; pre-rail compositions are honest stand-ins, **not** installed Behaviors |
| Post-rail shape | Identity = namespace + sealed class name; activated by **typed synapses** externally; body allowlist = Behavior API + Abstractions + selected contracts + approved BCL |

### L7 — Package / tree homes (from package-graph grill)

| # | Artifact | Layer | Status | Package / tree home | Public product API now? | Notes |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `DigitalBrainActivated` | Framework vocabulary | **Built** | **`DigitalBrain.Abstractions`** | Yes (substrate fact) | Not Flutter.Contracts; not samples; not Client/Kernel |
| 2 | Emit activation | Client programming model | Built substrate | **`IDigitalBrain.EmitAsync`** (existing) | Already Built | No new emit API |
| 3 | `ActivateDigitalBrain` | OS logic (pre-rail emitter) | **Built** sample L1 | **`samples/DigitalBrain.Compositions`** (`DigitalBrain.Shell`) | **No** NuGet | Sealed class; future Behavior identity; pull-invoked |
| 4 | `BootOnActivation` | OS logic (pre-rail reactor) | **Built** sample L1 | **`samples/DigitalBrain.Compositions`** | **No** NuGet | Compose `OpenHome`; not auto `IHandle` |
| 5 | `OpenHome` / `PostAuthBootstrap` / `NavigateShell` | OS logic samples | Built samples | same compositions tree | Sample only | Reuse open-home constants; keep names |
| 6 | First-screen vocabulary | Module vocabulary | Built | **`DigitalBrain.Modules.Flutter.Contracts`** (`IShell`, `OpenScene`, `SceneOpened`) | Already Built | UI is behavior *over* vocabulary |
| 7 | Install rail | Product rail | **Designed unbuilt** | Facts → Abstractions (or later thin leaf); load → Kernel-internal; author → Client | **No** | **Must not invent `IBehavior*` packages** |
| 8 | Behavior identity | OS | Designed shape pre-rail | namespace + sealed class (compositions preview) | Sample shape only | One public sealed class per file |
| 9 | Activation→UI BDD | Test | **L1 green** (pull) | **`tests/DigitalBrain.Compositions.Tests`** primary | N/A | `BehaviorOsActivationBoot` + honesty seals; journals oracles |
| 10 | L0 graph honesty | Test | Built | **`DigitalBrain.Tests`** boundary/package pins | N/A | Compositions non-packable; no Kernel on compositions |
| 11 | Testing helpers | Test harness | Optional later | **`DigitalBrain.Testing`** | No Behavior test interface | Hold hosting design §15 |
| 12 | Ui / Flutter host | Edge | Built projection / live residual | `hosts/DigitalBrain.Ui`, `clients/*` | Edge APIs only | Never own boot policy; projects `SceneOpened` only |
| 13 | Compositions NuGet | Packaging | Rejected pre-rail | samples only | **No** | Stay samples; not installed Behaviors |
| 14 | AccountEnrichment | Sample process neuron | Built sample | `samples/DigitalBrain.AccountEnrichment` | Sample only | Not boot; not Behavior install |
| 15 | Auto `IHandle` reaction | OS / rail | **Designed residual** | Post-rail handler host TBD | **No** | Pull compositions remain L1 truth |

**Do not** create a `DigitalBrain.Behaviors` product package until the rail has a consumer-backed package shape and human-approval path.

---

## 1. Framework vs OS split

| Layer | Owns | Must not |
| --- | --- | --- |
| **Framework** (Kernel, Abstractions, Client, generator, Testing) | Neuron/synapse mechanics, journals, owner bounds, `IDigitalBrain` Get/Send/Emit; substrate-wide facts such as `DigitalBrainActivated` | Login chrome, scene trees, CRM, product prompts, shell policy |
| **Modules** (`.Contracts` + runtime) | Domain vocabulary: `IShell`/`IScene`, `OpenScene`/`SceneOpened`, … | Second client API; Behavior install rail; widgets in C# |
| **Operating system (Behaviors)** | Product logic over existing vocabulary — including first screen | New synapse vocabulary without rebuild; Kernel domain knowledge; dispatch-by-name |
| **Edges** (Ui HTTP/SSE, Mcp, Flutter host) | Northbound projection of committed facts | Business logic; Behavior theater that skips synapses |
| **BDD tests** | Product sentences on journals + edge + host projection | Private field theater; mock Kernel grains as product contract |

**Programming model honesty:** ordinary C# over `IDigitalBrain` + contracts is both the pre-rail composition shape and the post-rail Behavior body. Identity = namespace + class name. **No `IBehavior` product API.** Pre-rail L1 compositions are **Built samples**, not installed Behaviors.

---

## 2. North-star product sentence

```gherkin
Given DigitalBrain is activated for an owner
When ActivateDigitalBrain commits DigitalBrainActivated (broadcast)
And BootOnActivation reacts (pre-rail: pull-invoked RunAsync; post-rail Designed: IHandle)
Then SceneOpened for home is journaled (SceneKey "home", Title "Home")
And Ui edge / Flutter may project SceneOpened (Built projection path)
```

**L1 status:** journals green via `BehaviorOsActivationBoot` (see L1 product sentence table).  
Evidence oracles: typed journals (`DigitalBrainActivated`, `SceneOpened`), Ui SSE where Built, Dart host projection where Built — never “it compiled.”  
**Not claimed:** auto handler reaction, install rail, product AppHost OS Healthy.

---

## 3. Activation fact (detail under L1)

### What activation is not

- Not a method reply, not `CapabilityRequested`, not a login credential event.
- Not Kernel “session started” domain knowledge (no login words in Kernel).
- Not automatic host `Program.cs` theater without a journaled fact.
- Not Flutter vocabulary (first-five pin stays intact).

### Home grill (summary)

| Option | Verdict |
| --- | --- |
| `DigitalBrain.Abstractions` | **Locked (L1) — Built** |
| `Flutter.Contracts` | **Reject** — UI vocabulary ≠ brain activation |
| New OS contracts package | **Hold** until lifecycle family grows |
| Compositions-only type | **Reject** — not durable shared vocabulary |

---

## 4. Emitter and reactor chain (detail under L2–L3)

```text
owner-scoped IDigitalBrain available (Connect / AddDigitalBrainClient / TestBrain — wiring only)
  → ActivateDigitalBrain.RunAsync(brain)          // EMITTER (L2) — Built pull
  → brain.EmitAsync(DigitalBrainActivated)        // journaled broadcast via session
  → BootOnActivation (composition)                // REACTOR (L3) — Built pull; IHandle Designed
  → IShell.Open(OpenScene home/Home)              // OpenHome constants
  → SceneOpened                                    // module neuron fact
  → Ui edge SSE / Flutter projects first screen    // edge only (L4) — SceneOpened only
```

| Phase | Who emits | How |
| --- | --- | --- |
| **Pre-rail** | `ActivateDigitalBrain` composition | `EmitAsync` — **Built** |
| **Production invoker** | Residual — tests/scripts first; edge may *invoke* composition after owner bind, but **does not invent the fact** | Never AppHost product rules |
| **Post-rail** | Same fact; may become approved Behavior | Typed activation, not name dispatch; auto `IHandle` **Designed** |

**Do not** put product emit logic in Kernel. **Do not** invent a Kernel “login completed” synapse.

---

## 5. First screen and auth boundary (detail under L5)

| Decision | Choice |
| --- | --- |
| First screen | Shell home via `OpenHome` |
| Login as auth Behavior | **Reject** |
| Passwords/tokens in journals | **Never** |
| `PostAuthBootstrap` | Keep name for richer post-auth orchestration; not the activation emitter; not IdP; body may open home today |

---

## 6. Flutter reaction path (detail under L4)

```text
OS (composition/Behavior)
  IDigitalBrain.Get<IShell>(shell).Open(OpenScene home)
        │
        ▼
ShellNeuron (module runtime) ──EmitAsync──► SceneOpened  (journal truth)
        │
        ▼
hosts/DigitalBrain.Ui  SSE GET /shells/{shell}/events
  host-private session journal read
  projects event: scene-opened only
        │
        ▼
clients/digitalbrain_flutter (+ shell chrome)
  SSE parse → ShellSurfaceController → pixels (key/title)
```

| Concern | Owner |
| --- | --- |
| Decide *which* scene | OS composition/Behavior |
| Open scene command | Flutter vocabulary `IShell.Open` / `OpenScene` |
| Fact of open | `SceneOpened` (`flutter.scene-opened`) |
| Project to host | Ui edge SSE (Built L1) |
| Pixels | Dart/Flutter host only — never widgets in C# Contracts |

Dual mutators (composition `IShell.Open` vs edge `POST …/scenes`) both journal the same `SceneOpened`. **Product BDD prefers the OS composition path.** Keep edge POST for tests; do not teach Flutter activation. **Flutter still observes `SceneOpened` only** — never `DigitalBrainActivated` over SSE.

---

## 7. Relationship map (existing compositions)

| Artifact | Status | Relation to Behavior OS |
| --- | --- | --- |
| `ActivateDigitalBrain` | **Built** pre-rail L1 sample | **Emitter** of `DigitalBrainActivated` (L2) |
| `BootOnActivation` | **Built** pre-rail L1 sample | **Reactor** → open home (L3); pull, not auto `IHandle` |
| `OpenHome` | Built sample | **Reuse** as open-home primitive |
| `PostAuthBootstrap` | Built sample; body ≈ open home | **Keep name**; not activation emitter; not IdP |
| `NavigateShell` | Built multi-scene helper | Shell utility; not activation boot |
| Surfaces (`CountdownSurface`, …) | Built multi-module compositions | Later OS apps; not first vertical boot |
| AccountEnrichment sample | Process neuron (Kernel OK) | **Not** a composition; not install rail |
| `IDigitalBrain.EmitAsync` | Built | Pre-rail emit pipe |
| `IHandle<T>` / `IEmit<T>` | Built Abstractions | Substrate handler shape — **auto activation reaction still Designed residual**; not a Behavior API |
| Install rail | **Designed unbuilt** | Human-approved proposal path not shipped |

---

## 8. BDD examples

### 8.1 Activation → home (product) — **L1 green**

```gherkin
Given DigitalBrain is activated for an owner
When DigitalBrainActivated is committed (broadcast via ActivateDigitalBrain)
And BootOnActivation reacts (pre-rail: pull-invoked RunAsync)
Then SceneOpened with sceneKey "home" and title "Home" is journaled on the shell outgoing journal
And (optional edge) Ui SSE projects scene-opened with the same key/title
```

**Proofs:** `BehaviorOsActivationBoot.ActivationSynapseDrivesOsBehaviorToStartUi`,  
`BehaviorOsActivationBoot.ActivationCommittedObservesSceneOpenedHome`  
(default `[Fact]`, not Explicit).

### 8.2 Pre-rail honesty — **L1 green**

```gherkin
Given the Behavior install rail is unbuilt
When tests invoke ActivateDigitalBrain then BootOnActivation after EmitAsync
Then the product sentence holds without claiming installed Behaviors
```

**Proofs:** `BehaviorOsActivationHonesty.ActivationSynapseDrivesBootNotHostProgram`,  
`BehaviorOsActivationHonesty.NoBehaviorByNameDispatchApi`.

### 8.3 Auth non-goal

```gherkin
Given a principal is bound at the Ui edge
When DigitalBrainActivated is emitted for that OwnerId
Then no password, cookie, or token appears in any synapse journal
And authentication machinery remains at the edge
And Flutter never observes DigitalBrainActivated over SSE
```

### 8.4 Designed residual (not green product claims)

```gherkin
# Designed — auto reaction residual
Given DigitalBrainActivated is broadcast
When no pull BootOnActivation is invoked
Then an IHandle<DigitalBrainActivated> (or approved Behavior) would open home
# Not Built — do not claim default green for auto-react
```

Proofs that are not green yet stay `[Fact(Explicit = true, …)]` or red→green in owning projects — **never a red root gate**. Compositions.Tests owns the primary activation→UI sentence.

---

## 9. Explicit non-goals / must-not

- No `IBehavior` / `IBehaviorTest` / Behavior-by-name dispatch  
- No claim install/approval/rollback rail **Built**  
- No claim auto `IHandle` activation reaction **Built** (Designed residual)  
- No `IFlutter` god neuron; no widgets / `BuildContext` / Dart types in C# Contracts or Kernel  
- No ProbeHost, UiGateway-in-Kernel, Auto hosting, Dart→Orleans, MCP-as-UI bus, OTel-as-UI truth  
- No tokens/passwords in journals  
- No calendar `IReminder` invent; no Kernel domain/login vocabulary  
- No fake dual path: host open-scene that skips journals as product truth  
- No Flutter subscription to `DigitalBrainActivated`  
- No Connect/session/Ui-bind/AppHost/module-Activate as activation emitter  
- No ShellName / CommandId / Timestamp on activation payload  
- No mega-files >400 lines without Explicit hold  
- No edit-without-codegraph; no keep-without-grill  
- No invent new product beyond L1–L7 locks in this file  

---

## 10. Residual open questions (with recommendations)

| # | Question | Recommendation | Hold until |
| --- | --- | --- | --- |
| R1 | Who *invokes* `ActivateDigitalBrain` in production (script vs edge-after-IdP bind)? | Pre-rail: tests + deliberate C#. Prefer edge-after-owner-bind **invokes composition** over Kernel auto-emit. Emitter remains composition. | Production invoker design |
| R2 | Shell selection source | Not on activation fact; composition/edge env / test fixture (`DIGITALBRAIN_SHELL` pattern) | First production invoker |
| R3 | Should Ui SSE ever project activation? | **No** for first vertical (L4) — Flutter stays `SceneOpened` only | Consumer proof |
| R4 | Merge `PostAuthBootstrap` into `BootOnActivation`? | **Keep both names**; fold bodies only if dual product sentences proven | Compositions migrate wave |
| R5 | Post-rail handler host for non-neuron Behaviors / auto `IHandle`? | Architecture: synapse-activated Behaviors, not grains-as-Behaviors; **Designed residual** | Install rail (Designed) |
| R6 | Product journal observation on `IDigitalBrain`? | Remain **Designed** (Hold #7) | Non-UI consumer |
| R7 | Live product AppHost Healthy for OS surface? | Remain residual (Hold #6) | Live-aspire wave |
| R8 | Idempotency of double emit | Prefer journal-visible re-emit over hidden Kernel dedupe until a real once-only consumer | Production invoker |

Historical B0 grills are listed in the header table.

---

## 11. Implementation order (pointer only)

1. ~~Red BDD: `ActivateDigitalBrain` emit + `BootOnActivation` → `SceneOpened` home~~ **Done L1 green** (`BehaviorOsActivationBoot`).  
2. ~~`DigitalBrainActivated` in Abstractions + alias pin~~ **Built**.  
3. ~~`ActivateDigitalBrain` + `BootOnActivation` compositions; compose `OpenHome`~~ **Built samples**.  
4. Optional Ui SSE assert on OS composition path (edge path already Built for `SceneOpened`).  
5. Auto `IHandle` reaction remains **Designed residual**.  
6. Install rail remains **Designed** until human-approval + journaled proposal land.

---

## 12. Decision log (grill summary)

| Decision | Choice | Strongest counter | Defense / fold |
| --- | --- | --- | --- |
| Activation name | `DigitalBrainActivated` | Vague “session ready” | Product sentence uses “activated”; one fact type |
| Alias | `db.digitalbrain-activated` | Put under `flutter.*` | Not UI vocabulary; Abstractions uses `db.*` |
| Fields | `Owner` only | Optional ShellName / Correlation bag | Shell is composition concern; correlation on envelope |
| Package home | Abstractions | Flutter.Contracts / OS.Contracts for one type | Substrate-wide; allowlist always includes Abstractions; promote family later |
| Emitter | `ActivateDigitalBrain` → `EmitAsync` | Connect/session auto-emit “always works” | Auto-emit is Kernel OS domain + wrong lifecycle; honesty requires explicit fact |
| Reactor | `BootOnActivation` → `OpenHome` | Host Program auto-open | Must be synapse-driven + journal-proven (pre-rail pull) |
| Auto `IHandle` | Designed residual | Claim L1 auto-react | L1 is pull compositions; auto-wire needs rail/host design |
| Flutter | `SceneOpened` SSE only | Teach Dart activation | Dual observation bus; architecture projection model |
| First screen | home via `OpenHome` | Login chrome first | §4.6 auth is edge; login ≠ authenticate Behavior |
| Install rail | Designed unbuilt | Ship empty `IBehavior` | architecture §5 + hosting design forbid theater |
| Packages | table L7 | Promote compositions NuGet now | packages.md + boundary pins: samples until rail |

---

## 13. Grill form + board (B7 docs-honesty)

### Recommendation form

```
Recommendation: update design.md Built vs Designed honesty after L1 green:
  DigitalBrainActivated / ActivateDigitalBrain / BootOnActivation = Built pre-rail L1;
  product sentence green via BehaviorOsActivationBoot + honesty seals;
  install rail still Designed unbuilt;
  auto IHandle reaction = Designed residual;
  Flutter still SceneOpened only;
  residual grills kept; scorecard is live residual board;
  no new product invented.

Strongest argument against:
  Calling pre-rail samples "Built" risks readers equating them with installed Behaviors
  or auto-reacting OS boot in product AppHost.

Defense / fold:
  Defend with honesty board: Built samples/L1 + explicit "not installed Behavior / not IHandle
  auto / not AppHost OS Healthy". Matches architecture.md activation section and scorecard
  post-B6 board. Docs-only fold; no product C# in this write.

Evidence:
  codegraph: DigitalBrainActivated BootOnActivation BehaviorOsActivationBoot —
  types present; Boot tests pull both compositions; ShellEventFeed SceneOpened only;
  no compositions IHandle for DigitalBrainActivated.
  Residuals: activation-synapse, emitter, flutter-reaction, package-graph grills + scorecard.
```

### Grill board (13)

1. **What does this thing do?** Updates Behavior OS design.md so Built vs Designed matches L1-green tree — activation fact + pull compositions journal-proven; rail and auto-react still Designed.
2. **Framework vocab, module neuron, OS behavior, edge, or test witness?** Activation fact = **framework vocabulary (Built)**. Emitter/reactor = **OS pre-rail compositions (Built samples)**. Open path = **module vocabulary**. Flutter pixels = **edge** (`SceneOpened` only). Proofs = **test witness** (`BehaviorOsActivationBoot` / Honesty).
3. **Consumer today?** Green Compositions.Tests; architecture/packages/CLAUDE honesty; scorecard. Runtime product AppHost does **not** auto-boot OS.
4. **Does architecture place it here?** Yes — architecture already states Built pre-rail L1; this file was stale “absent/Designed” codegraph. Align, do not invent.
5. **If UI-related: which synapse does Flutter react to? Which vocabulary opens the screen?** Reacts to **`SceneOpened` only**. Opens via **`IShell.Open(OpenScene)`** with **`home` / `Home`**. Still not `DigitalBrainActivated`.
6. **What would break if we deleted it?** Stale “absent” claims thrash implementers and contradict architecture/scorecard.
7. **Does this invent Behavior install rail without human-approval design?** **No** — L6 stays Designed unbuilt.
8. **Does Kernel/Hosting learn a domain word it must not know?** **No** — emitter/reactor remain compositions; module `Activate` is still DI-only.
9. **Proof BDD + journal/edge, or compile-only theater?** Journal BDD green: `ActivationSynapseDrivesOsBehaviorToStartUi`, `ActivationCommittedObservesSceneOpenedHome`. Auto-`IHandle` not claimed.
10. **What did I claim without a command?** Did not re-run root `dotnet build`/`dotnet test` (docs-only write scope). Codegraph + on-disk tests/types + HEAD/porcelain are the oracles. **No new green-gate claim beyond existing L1 test names.**
11. **What changed that I did not change?** Foreign tree may include product C# and other docs; this agent edits **only** this design.md.
12. **Could this live one layer in/out?** In (Kernel auto-activate) **reject**. Out (Flutter owns activation) **reject**. Pull L1 vs auto-`IHandle` residual correctly split.
13. **Would a new engineer find the right home by reading vision + architecture alone?** Yes if this file matches architecture L1 Built board; B7 honesty removes the stale “type absent” contradiction.

### Diff-grill (this fold)

1. **What did I add that has no consumer today?** Honesty board + L1 test-name table — consumers are engineers/agents; no new runtime API.
2. **What did I claim without running a command?** Type presence and test methods via codegraph/read; no full root gate this cycle.
3. **What changed that I did not change?** Product compositions/Abstractions/tests and other docs may already be green elsewhere — **not** edited here.

---

## 14. One-line summary

**`DigitalBrainActivated` (Abstractions, Built) is emitted by pre-rail `ActivateDigitalBrain` via `EmitAsync`; pull `BootOnActivation` opens shell home through `OpenHome`/`IShell` (L1 green: `BehaviorOsActivationBoot`); Flutter paints only SSE `SceneOpened`; auto `IHandle` reaction and install rail stay Designed unbuilt — no `IBehavior*` theater.**
