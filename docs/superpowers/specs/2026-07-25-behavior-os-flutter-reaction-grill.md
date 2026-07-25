# Behavior OS — Flutter reaction grill residual (Wave B0 agent 5)

Mission: `flutter-react` design only.  
Write scope: this residual. No product code. No `behavior-os-design.md` edits.  
Vision restated: **Framework = neurons + synapses; OS = behaviors (including UI); activation is a synapse Flutter does not consume — Flutter reacts to projected shell facts.**

**Ground (this cycle):**

| Field | Value |
| --- | --- |
| HEAD | `7ffaa21a415ed676ea4735cab06fa2de29a2b4d4` |
| Branch | `agent/digitalbrain-hosting-testing` |
| Porcelain | clean |
| Agent | Wave B0 agent 5 (`flutter-react`) |

---

## Design question

**How does Flutter present the first screen when the activation chain fires?**

---

## Hard recommendation

| Decision | Recommendation | Fold if |
| --- | --- | --- |
| Flutter knows `DigitalBrainActivated`? | **NO** | Architecture invents a second public observation bus for Dart (reject) |
| First product OS screen | **Shell home** — `SceneKey = "home"`, `Title = "Home"` (align `OpenHome`) | Real multi-principal IdP edge ships and product sentence *requires* login chrome as OS scene (not today) |
| Reaction path for product sentence | **OS composition / future Behavior → `IShell.Open` → `SceneOpened` → Ui SSE → Dart `watchShellEvents` → `ShellSurfaceController` / `ShellSurfaceHome`** | Evidence shows SSE cannot project composition-path opens (false today — L1 proves both paths) |
| Edge `POST …/scenes` open-scene | **Keep** as northbound mutator for host chrome tests and optional host tooling | Someone claims it is the *only* product boot path (fold that claim; do not delete the route) |
| Invent `login` scene key for first vertical | **NO** | Architecture §4.6 Auth edge is reversed in writing |

---

## Codegraph (mandatory paste)

```
Codegraph query: SceneOpened ShellEventFeed watchShellEvents ShellSurfaceHome OpenHome IShell edge openScene dual path
What it does (1 sentence): Two mutators (HTTP open-scene edge vs IDigitalBrain/IShell.Open composition) both reach ShellNeuron.Open and journal SceneOpened; Ui ShellEventFeed SSE projects only SceneOpened to Dart watchShellEvents → ShellSurfaceController / ShellSurfaceHome pixels.
Callers / consumers:
  - OpenScene: UiEndpoints, ShellNeuron, IShell; compositions OpenHome / PostAuthBootstrap / NavigateShell / surfaces; Flutter + Ui L1 tests
  - ShellEventFeed: UiEndpoints GET shell events
  - openScene (Dart): digitalbrain_host --open only; edge_client tests
  - watchShellEvents: shell main, headless host, edge tests
  - ShellSurfaceHome: ShellSurfaceApp home chrome
Dependents / blast radius:
  - ShellEventFeed: 1 production caller (UiEndpoints); vocabulary L0 pin
  - IShell: contracts L0 + compositions + Ui/Flutter L1
  - openScene Dart: optional host boot flag — not Desktop main path
Dual paths (paste carefully):
  Path A (product OS sentence — prefer):
    OS composition / future Behavior
      → brain.Get<IShell>(shell).Open(new OpenScene(..., "home", "Home"))
      → ShellNeuron.Open → EmitAsync(SceneOpened)
      → shell outgoing journal
      → ShellEventFeed.WriteSceneOpenedSseAsync (poll host-private session journal)
      → SSE event "scene-opened"
      → DigitalBrainUiEdgeClient.watchShellEvents
      → ShellSurfaceController.apply → ShellSurfaceHome list tile
  Path B (edge mutator — keep for tests / host tooling):
    POST /shells/{shell}/scenes  (UiEndpoints → same IShell.Open)
      → same SceneOpened journal + same SSE projection
    Optional Dart openScene / headless --open:client-side Path B only
Public vs internal:
  - Public module vocab: IShell, OpenScene, SceneOpened (DigitalBrain.Flutter)
  - Internal edge: UiEndpoints, ShellEventFeed, edge OpenSceneRequest DTO
  - Client pure Dart: DigitalBrainUiEdgeClient, ShellSurfaceController (not Framework)
  - Desktop chrome: ShellSurfaceHome (pixels only)
Framework vs OS vs edge:
  - Framework: journal mechanics only (no scene keys)
  - Module vocab: IShell / OpenScene / SceneOpened
  - OS behavior: OpenHome / PostAuthBootstrap today (samples); future Behavior reacts to activation
  - Edge: HTTP/SSE projection; no business logic
```

**Evidence anchors (tree, not invent):**

| Piece | Home |
| --- | --- |
| `IShell.Open` / `SceneOpened` | `modules/DigitalBrain.Modules.Flutter.Contracts` |
| `ShellNeuron.Open` → `EmitAsync(SceneOpened)` | `modules/DigitalBrain.Modules.Flutter/ShellNeuron.cs` |
| Composition open home | `samples/DigitalBrain.Compositions/Shell/OpenHome.cs` (`SceneKey="home"`, `Title="Home"`) |
| Post-auth opens home via IShell | `samples/DigitalBrain.Compositions/Shell/PostAuthBootstrap.cs` |
| Edge POST open-scene | `hosts/DigitalBrain.Ui/UiEndpoints.cs` |
| SSE project SceneOpened only | `hosts/DigitalBrain.Ui/ShellEventFeed.cs` |
| Dual L1 | `tests/DigitalBrain.Ui.Tests/UiEdgeRoundTrip.cs` — HTTP path **and** `IDigitalBrain` mutator path both journal + SSE |
| Composition L1 | `tests/DigitalBrain.Compositions.Tests/ShellAndSurfaceCompositions.cs` — OpenHome / PostAuthBootstrap |
| Desktop reacts only to SSE stream | `clients/digitalbrain_flutter/shell/lib/main.dart` — `watchShellEvents` only; **no** open on start |
| Headless optional Path B | `clients/digitalbrain_flutter/bin/digitalbrain_host.dart` — `--open` then watch |

---

## Activation chain vs Flutter (layer split)

Target product chain (campaign north-star, names for activation fact are Wave B0 synapse-vocab residual):

```text
DigitalBrain activated (owner-scoped)
  → DigitalBrainActivated (or architecture-named) Synapse  [Framework / OS emitter — NOT Flutter]
  → OS Behavior reacts                                   [OS]
  → IShell.Open(OpenScene(home, Home))                   [module vocab request]
  → SceneOpened fact                                     [module vocab synapse]
  → Ui edge SSE scene-opened                             [edge projection]
  → Flutter host ShellSurfaceController / chrome         [edge client pixels]
```

**Flutter’s job ends at the last two arrows.** It must not subscribe to activation, own IdP, or invent scene keys for auth.

---

## Grill: first screen

### Does Flutter need to know `DigitalBrainActivated`?

**Recommendation: NO.**

| Argument | Side |
| --- | --- |
| Strongest for YES | One less hop — host `main` listens for activation and draws chrome immediately |
| Defense (fold YES) | That is **host hand-wire theater**. Architecture §4.6: Flutter rebuild is projection of committed journals; first vertical input is `SceneOpened` (key + title + sequence). Auth/activation are not pixel vocabulary. SSE today projects **only** `SceneOpened` (`ShellEventFeed.ProjectSceneOpened`). Teaching Dart about activation invents dual observation and couples client to Framework/OS facts. |
| Evidence | Codegraph dual path; Desktop `main` only `watchShellEvents`; Ui L1 mutator path proves composition-side open reaches SSE without Dart knowing the mutator |

### Login scene vs home

**Recommendation: first product OS screen = shell home (`home` / `Home`), not IdP login chrome as Behavior.**

| Concern | Owner (architecture §4.6 Auth edge) |
| --- | --- |
| Credentials, IdP, cookies, token mint/validate | **Edge only** — Forbidden in composition / Behavior |
| Principal → `OwnerId` | Edge owns; composition receives ambient owner |
| Shell/scene UX after bind | Composition / future Behavior via Flutter vocabulary |
| Passwords / tokens in journals | **Never** |

Architecture already names the phrase **post-auth composition**: edge authenticates and binds owner; composition orchestrates shell after bind. `PostAuthBootstrap` already opens home via `IShell` with the same key/title as `OpenHome` — not a `login` scene.

Inventing `login` as first Behavior-owned screen would:

1. Contradict “login is not a grain auth authority and not a Behavior that authenticates.”
2. Push credentials UX into OS logic that must not hold tokens.
3. Create a scene key with no Built chrome beyond key/title list (empty product value).
4. Drift from proven constants `OpenHome.SceneKey` / `OpenHome.SceneTitle`.

IdP login chrome (when Built-live multi-principal edge exists) stays **edge pixels or external IdP redirect**, not a journaled `SceneOpened` that pretends to authenticate.

### Dual path: prefer which open?

Both paths journal the **same** `SceneOpened` and SSE projects both (L1).

| Path | Role |
| --- | --- |
| **OS composition / Behavior → `IShell.Open`** | **Product sentence.** Activation reaction lives here. Aligns “OS owns logic including UI.” |
| **Ui `POST` open-scene** | **Northbound mutator / test harness / optional host tooling.** Same vocabulary; not the activation reaction home. |
| **Dart `openScene` / headless `--open`** | Host chrome test convenience; **not** product boot. Desktop main deliberately does not open. |

**Prefer OS composition path for product BDD.** Keep edge POST; do not delete dual mutators that converge on one fact — delete *claims* that Flutter boot = HTTP open-scene from `main`.

---

## Recommendation form (§2)

```
Recommendation: keep Flutter reaction = SceneOpened via SSE only;
  first screen = OpenHome home/Home;
  product open = OS composition/Behavior → IShell.Open;
  keep edge POST open-scene for tests; do not teach Flutter DigitalBrainActivated;
  do not invent login scene key for first vertical.

Strongest argument against:
  Campaign north-star prose says "login / first screen" and Gherkin leaves
  "login or shell home — design decides," so login might be the honest first paint
  for a real OS.

Defense / fold:
  Architecture §4.6 Auth edge already decided: login is edge, not Behavior that
  authenticates. PostAuthBootstrap + OpenHome prove home as post-bind shell open.
  Fold "login as first OS scene" until multi-principal IdP edge is Built and a
  non-auth scene (e.g. "choose account") has a real consumer. Prefer home now.

Evidence:
  codegraph dual path paste above;
  architecture.md §4.6 Auth edge + Projection model;
  OpenHome.cs SceneKey/Title;
  UiEdgeRoundTrip DigitalBrainMutatorJournalsAndSseProjectsWithoutRestart;
  shell main.dart watch-only.
```

---

## Assess template (§6)

```
Scope: Flutter reaction path + first product OS screen when activation chain fires
  (design residual only; no code).

Codegraph query + blast radius (paste §3):
  Query: "SceneOpened ShellEventFeed watchShellEvents ShellSurfaceHome OpenHome IShell edge openScene dual path"
  Blast: dual mutators → one SceneOpened → SSE-only Flutter apply; openScene Dart is optional Path B;
  ShellSurfaceHome has chrome tests under shell/test (codegraph note on missing coverage is incomplete —
  shell_chrome_test.dart exists).

What it does (1 sentence):
  Flutter presents open scenes by projecting SSE SceneOpened facts into a key/title shell list;
  OS logic opens those scenes via IShell after activation (not via Flutter knowing activation).

Layer: edge (projection) + os-behavior (open decision) + module-vocab (IShell/SceneOpened)
  — not framework domain.

Consumer today:
  - L1 Ui/Flutter/Compositions tests
  - Desktop/Headless hosts (SSE apply)
  - samples OpenHome / PostAuthBootstrap
  - Product person: none for activation→UI full chain (Designed Behavior rail)

Architecture home (section):
  docs/architecture.md §4.6 Flutter (projection model, northbound path, OS compositions)
  + §4.6 Auth edge. Deliberate extension for Behavior OS activation reaction home =
  future Behavior / pre-rail composition — not Flutter client.

Activation synapse? (name / none / invent with red):
  none for Flutter. Activation fact name is synapse-vocab residual (agents 3–4).
  Flutter reaction synapse = SceneOpened (Built).

Flutter reaction path:
  SceneOpened → ShellEventFeed SSE "scene-opened" → watchShellEvents
  → ShellSurfaceController.apply → ShellSurfaceHome list.

BDD scenario (Given/When/Then) — design shape for B0 bdd-red (not written this cycle):
  Given DigitalBrain is activated for an owner
  When the activation synapse is committed
  Then an OS composition/behavior opens shell home via IShell
  And SceneOpened(home, Home) is journaled
  And SSE projects scene-opened
  And the Flutter surface shows scene key "home" title "Home"
  # Explicit hold until activation fact + Behavior reaction exist

Public surface:
  IShell, OpenScene, SceneOpened (module). Edge HTTP/SSE routes (Built). No new public API this residual.

Implementation hidden? Y — ShellNeuron, ShellEventFeed, session journal poll are internal.
  Leaks: none proposed. Risk: treating headless --open as product sentence (document as test-only).

Belongs here? Y for residual. N for putting activation in Dart or login auth in Behavior
  → re-home auth to edge; re-home open decision to OS behavior.

Aligns with framework=neurons+synapses, OS=behaviors? Y

Dual path / host hand-wire?
  Dual mutators converge on SceneOpened (keep both).
  Host hand-wire to avoid: main() open-scene / special-case activation in Flutter.
  Product prefer composition IShell.Open; edge POST remains for host chrome tests.

Delete candidates:
  - Claims that first screen is login Behavior (doc claim only)
  - Future dual SSE parsers / dual projection controllers if reintroduced (already collapsed prior campaign)
  - Do not delete edge POST or openScene Dart without replacing test consumers

Recommendation form (§2): see above.

Verify:
  No product code this cycle. Codegraph + tree read + architecture quote only.
  Root gate not claimed. No live Aspire claim.

Grill 13: see board below.
```

---

## Grill board (13)

1. **What does this thing do?**  
   Flutter projects committed `SceneOpened` facts over SSE into key/title shell chrome; OS logic decides *when* and *which* scene opens after activation.

2. **Framework vocab, module neuron, OS behavior, edge, or test witness?**  
   Reaction pixels = **edge client**. Open decision = **OS behavior** (composition today). Facts = **module neuron/synapse**. Activation fact = Framework/OS (not Flutter).

3. **Consumer today?**  
   Ui/Flutter/Compositions L1 tests; Desktop/Headless hosts; sample compositions. No installed Behavior consumer (rail unbuilt).

4. **Does architecture.md place it here?**  
   Yes — §4.6 projection model (`SceneOpened` first vertical input), northbound path (HTTP/SSE edge), Auth edge (login not Behavior), OS compositions (`OpenHome` / `PostAuthBootstrap`). Silent invent would be “Flutter listens to DigitalBrainActivated” or “login scene is first Behavior.”

5. **If UI-related: which synapse does Flutter react to? Which vocabulary opens the screen?**  
   Reacts to **`SceneOpened`** (SSE `scene-opened`). Opens via **`IShell.Open(OpenScene)`** with key/title **`home` / `Home`**.

6. **What would break if we deleted it?**  
   Delete SSE `SceneOpened` projection → hosts show “No scenes open” forever. Delete `IShell`/`SceneOpened` → compositions and Ui edge fail compile/L1. Delete edge POST only → Path B tests fail; Path A product sentence can survive. Delete Dart `openScene` only → headless `--open` fails; Desktop main unaffected.

7. **Does this invent Behavior install rail without human-approval design?**  
   No. Residual is design grill only; compositions remain pre-rail samples.

8. **Does Kernel/Hosting learn a domain word it must not know?**  
   No. No Kernel/Hosting change. Scene keys stay in compositions/module contracts usage, not Kernel.

9. **Proof BDD + journal/edge, or compile-only theater?**  
   Built proofs already: journal `SceneOpened` (Flutter L1), composition OpenHome, dual mutator SSE (Ui L1), Dart `ShellSurfaceController` tests. **Missing product BDD:** activation → OS open → first screen (Designed; Explicit red for later B0 bdd-red agents).

10. **What did I claim without a command?**  
    Did not re-run root build/test or live Aspire. Claimed dual path and home constants from codegraph + file reads + existing L1 DisplayNames only. **No green-gate claim.**

11. **What changed that I did not change?**  
    Porcelain clean at cycle start/end; only this residual file is in scope for this agent to add.

12. **Could this live one layer in/out?**  
    One layer in (Framework) = Kernel knows home scenes — **reject**. One layer out (Flutter invents open on activate) = host theater — **reject**. Correct: OS decides open; module journals fact; edge projects; Flutter paints.

13. **Would a new engineer find the right home by reading vision + architecture alone?**  
    Mostly yes for projection (`SceneOpened` + edge). Risk: campaign prose “login / first screen” without this residual. **This file pins home + no Flutter activation** so B0 design doc (agents 13–16) can quote a single hard decision.

---

## Diff-grill (this residual)

1. **What did I add that has no consumer today?**  
   A design residual document. Consumer = Wave B0 design/scorecard agents and B3 behavior implementers. No runtime consumer (correct for design-only).

2. **What did I claim without running a command?**  
   Full dual-path behavior and L1 proof content inferred from source + codegraph, not re-executed tests this cycle.

3. **What changed that I did not change?**  
   Nothing foreign in porcelain at cycle boundaries.

---

## Downstream handoff

| Next mission | Use this residual as |
| --- | --- |
| synapse-vocab (agents 3–4) | Flutter does **not** consume activation fact; name/home of activation unconstrained by Dart |
| bdd-red (agents 7–10) | First screen assertions: `SceneKey == "home"`, `Title == "Home"`; SSE projection optional second tier; **no** Flutter activation subscription |
| behavior-impl (B3) | React to activation → `IShell.Open` OpenHome constants; migrate `OpenHome` / `PostAuthBootstrap` shape |
| edge-project (B4) | Keep SSE `SceneOpened` only; do not add activation events to SSE without new grill + red |
| docs-honesty (13–16) | First-screen decision = **shell home**; reaction path = **composition → SceneOpened → SSE** |

---

## Residual holds (honest)

| Hold | Status |
| --- | --- |
| Activation synapse exact type/package | Out of this mission (agents 3–4) |
| Behavior install rail | Designed / unbuilt — compositions stand in |
| Multi-principal IdP edge | Designed — not first OS scene |
| Live product AppHost OS Healthy | Residual unproven (§4.6) — not required to lock reaction design |
| Richer scene descriptors beyond key/title | Designed — first screen still key/title |
| Product journal observation on `IDigitalBrain` | Designed — edge uses host-private poll |

---

## One-line lock for scorecard / design

> **First product OS screen is shell home (`home`/`Home` via `IShell.Open`); Flutter reacts only to SSE `SceneOpened`; activation is OS-side; edge POST open-scene stays for tests; login is edge auth, not a Behavior scene.**
