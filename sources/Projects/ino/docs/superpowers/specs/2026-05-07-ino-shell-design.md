# ino-shell — design spec

**Status**: brainstormed, ready for plan
**Date**: 2026-05-07
**Source design**: `docs/ino-design/ino-shell.html` (handoff from Claude Design)
**Source README**: `https://api.anthropic.com/v1/design/h/qlIgyjm7Z6phK8AABFG4Yg` (extracted to `.scratch/design/extracted/ino/README.md`)

## Goal

Replace the current `/brain` experience visually by shipping a new `/shell` route that renders the ino-shell prototype in Flutter, then validate the **neuron** and **synapse** abstractions on screen with a fully mocked Tokyo storyboard before any LLM is wired up.

The shell promotes synapses from "types you can see on the lobes" to "messages you can watch fly between neurons." Decay becomes a first-class visual (gold-only feedback). The persona orb becomes the input surface. The brain itself is the loading screen.

LLM hookup is **out of scope** for this slice — it lands next, and replaces the storyboard runner with the live `BrainStreamService`.

## Decisions (locked during brainstorm)

1. **Target surface**: Flutter web, served from `Ino.Kernel/wwwroot`. Stay on `three_js: ^0.3.0` (already in `clients/ino.flutter/pubspec.yaml`) for the brain canvas.
2. **Slice 1 scope**: full shell parity — brain + orb + composition canvas + timeline density river + inspector drawer + tokens panel + synapse mid-flight tooltip + Tokyo storyboard + replan.
3. **Mock data path**: a Reqnroll feature file in `domains/travel/Ino.Domains.Travel.Tests/Features/Tokyo.feature` is the single source of truth. It drives both a behavioural test (xUnit + Reqnroll against `TestCluster`) and a generated `tokyo.json` storyboard asset that the Flutter `DemoRunner` replays.
4. **Routing**: ship as new `/shell` GoRoute alongside `/brain`. `/brain` stays untouched until the new shell is proven; cutover is a later, separate change.
5. **Persona orb**: reuse the existing Rive asset `clients/ino.flutter/assets/rive/persona_orb.riv` via the existing `PersonaWidget`. Map demo events into `PersonaBloc` (existing `PersonaEmotion` enum already covers all six states: idle, listening, thinking, responding≈speaking, celebrating, confused). Visual delta from the prototype's CSS orb is accepted.
6. **Decay color rule**: gold (`#E8C56A`) is reserved for recall fires only. Pinning this rule visually communicates "memory was just touched" and decouples it from generic compute traffic.

## Architecture

```
+---------------------------- shell_screen.dart ---------------------------+
|                                                                          |
|  [shell_brain_canvas (three_js)]   <-- LAYER 1: 3D sphere of clusters   |
|  [vignette]                        <-- LAYER 2: radial gradient overlay |
|  [cluster labels]                  <-- LAYER 3: projected text labels   |
|                                                                          |
|  [shell_topbar]                    <-- LAYER 20: tokens / pill / orb /   |
|                                                  replan / play          |
|  [shell_compose]                   <-- LAYER 5: cards in middle         |
|  [shell_timeline]                  <-- LAYER 15: density river bottom   |
|  [shell_inspector_drawer]          <-- LAYER 30: right slide-in         |
|  [shell_tokens_panel]              <-- LAYER 30: left slide-in          |
|  [shell_synapse_tooltip]           <-- LAYER 40: mid-flight overlay     |
|  [demo_pip]                        <-- LAYER 18: t = 0.0s, stage label  |
|                                                                          |
+--------------------------------------------------------------------------+
                               |
        DemoRunner ------------+----------- BrainInspectorBloc
        (reads tokyo.json)                  TimelineBloc
                                            PersonaBloc
                                            InoBloc (cards/compose)
```

Z-ordering matches the prototype's `z-index` rules (1 → 40), translated to Flutter `Stack` order.

## Components

### New, under `lib/screens/shell/`

| File | Role |
|---|---|
| `shell_screen.dart` | Top-level Stack, wires BLoCs, owns DemoRunner lifecycle |
| `shell_topbar.dart` | Tokens button + cluster pill (left); persona mount + ghost input (center); replan + pause + replay + Play demo (right) |
| `shell_brain_canvas.dart` | three_js scene: jittered sphere of neurons, cluster glow halos, filament edges, comet spawner, raycaster, projected cluster labels |
| `shell_brain_topology.dart` | Cluster anchors (cortex, travel, recall, location, reminders, taxi, genesis, identity) + per-cluster alias lists. Ported from `docs/ino-design/src/data.js`. Replaces `clients/ino.flutter/lib/screens/brain/brain_topology.dart` for the shell route only |
| `shell_compose.dart` | Composition canvas — Stack of `ShellCard`s; arc-in entry from cluster center; morph; exit |
| `shell_card.dart` | Card chrome: title/sub/cluster strip + rows; row variants for flight, hotel, itinerary day, reminder; chevron emits `ReplayTrace` |
| `shell_timeline.dart` | CustomPainter density river (24h sparkline) + chips (Now/10m/Today/Week/Origin) + scrubber + life marks + time readout + pin-moment button |
| `shell_inspector_drawer.dart` | Right drawer — alias / domain / `grain://` id; last-20 synapses; decay sparkline; prompt corpus (LlmNeuron only); fire-test button |
| `shell_tokens_panel.dart` | Left drawer — palette / typography / motion / latency tokens + auto-focus toggle |
| `shell_synapse_tooltip.dart` | Mid-flight overlay (traceparent + decay + payload JSON) |
| `shell_theme.dart` | All design tokens as Dart constants (palette, glass, easing, durations, latency budgets) |
| `demo_runner.dart` | Reads `tokyo.json`, schedules `Timer`s on event `t` offsets, dispatches BLoC events |

### Reused (no rewrite)

- `state/persona_bloc.dart` — already has all six emotions; DemoRunner dispatches `PersonaEmotionChanged`
- `state/brain_inspector_bloc.dart` — selection + last-N events; extended with paused-synapse field if not already present
- `state/timeline_bloc.dart` — extended with `density: List<double>`, `lifeMarks: List<TimelineMark>` fields
- `persona/persona_widget.dart` — used inside `shell_topbar` at `size: 130`. The CustomPaint fallback path stays (it kicks in when the Rive load fails); Rive is the primary
- gRPC layer: untouched (slice 1 is mocked end-to-end)

### Untouched (parallel)

- `lib/screens/brain/*` — `/brain` route still works with the old screen until the new `/shell` is proven

## Brain canvas — sphere geometry & comet synapses

The abstraction-defining part of the design. Implementation port of `docs/ino-design/src/brain.js` with these constants and rules:

- **Sphere shell**: `SPHERE_R = 1.55`. Neurons placed by `clusterCenter.normalize() * SPHERE_R + jitter ∈ [-0.225, +0.225]³`, then re-projected to the shell with shell thickness `±0.18`.
- **Neuron mesh**: `SphereGeometry(0.03–0.055)` + `MeshBasicMaterial { color: clusterColor, transparent, opacity: 0.85 }`. Halo: additive-blended sphere at `3.2× radius`, opacity `0.0` baseline, surges to `0.67` on flare.
- **Idle pulse**: `1 + 0.06 * sin(t * 1.2 + per-neuron-phase)`. Flare scale: `+1.6 * flare`, decays at `1.6 / s`.
- **Cluster glow**: additive sphere at `clusterCenter * 0.95 * SPHERE_R`, base opacity `0.05`, surges to `0.37` on `fire(mag=1)`, decays at `0.9 / s`.
- **Filaments**: faint static bezier lines for `cortex↔{travel,recall,location,reminders,taxi,genesis,identity}` plus `travel↔{recall,location,reminders}` and `recall↔identity`. Opacity `0.05` with `0.04 * sin(t * 0.6)` modulation.
- **Comet synapse**:
  - Inputs: `from` alias, `to` alias, `payload: Map<String, dynamic>`, `gold: bool`, `dur: double = 0.42–0.60s`.
  - `from` and `to` resolved through alias map; ignores comet on miss.
  - Curve: `QuadraticBezierCurve3(a, mid, b)` where `mid = (a + b) / 2` then `mid.normalize() * (SPHERE_R + 0.42)`.
  - Tail: `Line` with 50-point `BufferGeometry`, additive-blended, opacity `0.10 + (1 - u) * 0.30`.
  - Head: `SphereGeometry(0.04)` + halo at `0.13`, color `cyan` default, `gold` if `gold == true`.
  - Side effects: `flareNode(fromMesh, 1)` on spawn, `flareNode(toMesh, 1)` at `dur * 0.7`, `fireCluster(fromCluster, 0.6)` on spawn, `fireCluster(toCluster, 1.0)` at `dur * 0.7`.
  - Click on head: `paused = true`, freeze head position, emit `SynapsePaused(syn, screenX, screenY)`. Document-wide click resumes and dismisses tooltip.
- **Camera**: orbit controls; auto-orbit at `0.05 rad/s` when no drag for 2s+; `focusCluster(id)` lerps `theta`, `phi` toward the cluster direction with smoothing factors `0.02` and `0.04`. Auto-focus gated by tokens panel toggle.
- **Cluster labels**: HTML/Flutter overlay positioned at `clusterCenter.normalize() * (SPHERE_R + 0.45)` projected to screen each frame. Opacity `max(0.2, 1 - projected.z)`.

Naming convention: synapses identify by **alias** (`PlanTrip`, `FindFlights`, `Preferences`, `Forecast`, etc.), not by Orleans grain ID. Aliases are stable user-facing identifiers; grain IDs are runtime addresses. The visual layer never sees a grain ID — `topology_grain_map` translates inbound stream events → aliases at the BLoC seam. This decoupling matters for slice 2 because grain IDs change across rebuilds and silos but aliases are part of the contract.

## Persona orb wiring

Existing `PersonaWidget(size: 130)` mounted in `shell_topbar`. Rive primary, CustomPaint fallback retained. DemoRunner emits `PersonaEmotionChanged(PersonaEmotion.X)` on storyboard `orb` events:

| Storyboard `state` | `PersonaEmotion` |
|---|---|
| `listening` | `listening` |
| `thinking` | `thinking` |
| `speaking` | `responding` |
| `celebrating` | `celebrating` |
| `confused` | `confused` |
| `idle` | `idle` |

**Risk**: `persona_orb.riv` state machine inputs are unknown until inspected. If the asset doesn't expose all six states, slice 1 maps as best-effort and documents the gap. We do **not** edit the .riv as part of this slice.

The ghost text input ("Hold to talk · or type a synapse") sits below the orb in `shell_topbar`, ~360px wide, glass-styled. Pressing Enter triggers Play demo (single-storyboard scope for slice 1; user input doesn't drive a real LLM yet).

## Composition canvas (cards)

Existing card widgets (`flight_card`, `hotel_card`, `event_card`, etc.) **are not reused for the shell** — the prototype's card chrome differs (glass, rows with cluster strips, recall tags, dim/highlight states). New `shell_card.dart` renders all four card kinds via row variants:

- **flight row**: code · route · duration · price · tag
- **hotel row**: name · area · note · price · tag (optional `dim`, `highlight`)
- **itinerary day row**: day · weather % · plan (optional `highlight`)
- **reminder row**: name · when · tag

Card entry: arc-in from the firing cluster's projected screen position (computed once on spawn), `240ms · spring · overshoot`. Morph (replan): swap-flash on the card border (`box-shadow: 0 0 0 6px rgba(232,197,106,0.18)` translated to a `BoxDecoration` animation), row replacement, no full re-mount. Exit: `clearCards()` on storyboard end isn't called — cards linger as ghost evidence of what happened.

Click chevron → emits `ReplayTrace(cardId)` → DemoRunner replays the card's authoring synapses as a fresh comet sequence, ~280ms apart.

## Timeline density river

`shell_timeline.dart` with CustomPainter:

- **Density**: 280-bucket sparkline (mock formula in slice 1: `0.18 + 0.32 * sin(t * π * 2.6 - 0.6) + 0.12 * sin(t * π * 11) + noise + recent burst`). Rendered as a filled path with `LinearGradient` (indigo → cyan, `opacity 0.6 → 0`).
- **Chips**: `Now / Last 10m / Today / This week / Origin`. Active chip in cyan; others in muted glass. Chip click jumps the time readout.
- **Scrubber**: vertical 1px line + draggable handle at `~92%` ("now"), gradient handle.
- **Life marks**: small pins along the river — origin, L1-born (green), pinned (gold), incident (red), now. Tap reveals label tooltip.
- **Pin moment**: button on the right; emits `PinMoment(t)` → adds a gold mark.

Timeline state moves into `TimelineBloc` (extended with `density`, `lifeMarks`). DemoRunner doesn't drive the timeline in slice 1 — life marks come from a static fixture that ships with `tokyo.json`.

## Inspector drawer

Right slide-in (`width: 420px`), opens on neuron click:

- Header: cluster-tinted dot · alias · domain · `grain://domain/alias/0xHEX`
- **Last 20 synapses** section: list of `{t, from|to, payload}` rows; gold tone if `recall: true`. Mock data per alias from `eventsFor(alias)` in `data.js`.
- **Decay map · 24h** section: big number (`87 / 100 · brightening on access`) + sparkline (mock 28-point curve).
- **Prompt corpus** section: only renders for LlmNeuron-flagged aliases. Read-only mono-font block, max-height scroll. Mock corpus per alias from `data.js` (`PROMPTS` map).
- **Fire test synapse** button: emits `FireTestSynapse(alias)` → DemoRunner spawns a self-test comet to a partner alias (e.g. PlanTrip ↔ Cortex).

## Tokens panel

Left slide-in (`width: 360px`), opens on top-left "tokens" button. Renders all tokens from `shell_theme.dart` for visual review:

- Palette swatches with hex labels
- Typography sample (Inter 24/600 heading; Inter 14/22 body; JetBrains Mono ID/payload)
- Motion table (ease, comet, card entry, idle pulse, camera orbit)
- Latency budget table (utterance → first comet ≤ 400ms; → first card ≤ 2.5s; → complete plan ≤ 6s; "spinners are banned")
- Auto-focus toggle (binds to `ShellBrainCanvas.setAutoFocus`)

This panel exists to make the design system inspectable in-app and to lock the rules during reviews — it's a tool, not a feature for end users.

## Synapse mid-flight tooltip

`shell_synapse_tooltip.dart` overlay shown when `BrainInspectorBloc.pausedSynapse != null`. Position: pause-click coords with `translate(-50%, -110%)`. Content:

```
synapse · paused mid-flight
PlanTrip → FindHotels
traceparent: 00-<rand-hex>-<rand-hex>-01
decay: 73 · compute   (or "decay: 81 · recall" gold)
{ "city": "Tokyo", "tier": "mid", "constraints": ["rain-friendly"] }
click to resume
```

Document-wide click outside tooltip → resume comet, dismiss tooltip.

## Demo storyboard pipeline

### Source of truth: `Tokyo.feature`

Located at `domains/travel/Ino.Domains.Travel.Tests/Features/Tokyo.feature`. One scenario per storyboard variant. Steps name aliases and timings; payload literals are inline.

```gherkin
Feature: Tokyo plan demo storyboard
  Scenario: Plan a 5-day Tokyo trip in late October
    Given a fresh ino brain with the v0.1 cluster set
    When the user says "Plan a 5-day Tokyo trip in late October, rain-friendly, mid-budget, leave from Kyiv."
    Then the persona is listening at +0.00s
    And the persona is thinking at +1.20s
    And Cortex synapses to PlanTrip at +1.20s with payload { intent: "plan_trip", city: "Tokyo" }
    And PlanTrip synapses to FindFlights at +1.60s with payload { from: "KBP", to: "NRT", when: "2026-10-22..27", tier: "mid" }
    And PlanTrip synapses to FindHotels at +1.62s with payload { city: "Tokyo", tier: "mid", constraints: ["rain-friendly"] }
    And PlanTrip synapses to FindPlaces at +1.64s with payload { city: "Tokyo", mood: "rain-friendly" }
    And Preferences synapses to PlanTrip at +2.00s gold with payload { ryokanBias: 0.62, hotelChainBias: -0.38, source: "recall.priorTrips" }
    And Forecast synapses to PlanTrip at +2.40s with payload { tokyo_oct: { d1: 0.22, d2: 0.61, d3: 0.78, d4: 0.30, d5: 0.18 } }
    And the flights card enters at +3.00s from travel
    And the hotels card enters at +3.80s from travel
    And the itinerary card enters at +4.60s from travel
    And PlanTrip synapses to VisaReminder at +5.40s with payload { topic: "visa", remindIn: "3 days" }
    And the reminder card enters at +5.50s from reminders
    And the persona is celebrating at +6.00s
    And the persona is idle at +6.20s

  Scenario: Make day 3 cheaper replan
    Given the previous plan is on screen
    When the user says "Make day 3 cheaper."
    Then the persona is thinking at +0.10s
    And Cortex synapses to PlanTrip at +0.30s with payload { intent: "refine", dim: "day3.budget" }
    And PlanTrip synapses to FindHotels at +0.55s with payload { day: 3, max: "mid-low", swap: true }
    And the hotels card morphs at +1.20s
    And the persona is idle at +1.40s
```

### Two consumers

**Consumer 1 — behavioural test** (`Ino.Domains.Travel.Tests`):
- xUnit + Reqnroll runs each scenario against `TestCluster` + `BddMockChatClient`.
- Step definitions assert: synapse type contracts (`ChatIntent`, `FindFlightsRequest`, etc. — not the storyboard alias literals), payload shapes, ordering. The alias names from the feature map to the canonical `IDomain`/`Neuron` types via a small alias→type table.
- Catches regressions in real neuron behaviour as we wire LLMs in slice 2 without changing the visual demo.

**Consumer 2 — visual demo runner** (Flutter):
- A custom Reqnroll plugin (or step-base recorder) emits `tokyo.json` per scenario into `clients/ino.flutter/assets/storyboards/`. Schema:

```jsonc
{
  "id": "tokyo",
  "label": "Tokyo plan · 6s",
  "duration_s": 6.4,
  "events": [
    { "t": 0.00, "kind": "orb",  "state": "listening" },
    { "t": 0.00, "kind": "utter", "text": "Plan a 5-day Tokyo trip ..." },
    { "t": 1.20, "kind": "orb",  "state": "thinking" },
    { "t": 1.20, "kind": "syn",  "from": "Cortex",   "to": "PlanTrip",   "payload": {...} },
    { "t": 1.60, "kind": "syn",  "from": "PlanTrip", "to": "FindFlights","payload": {...} },
    ...
    { "t": 3.00, "kind": "card", "id": "flights", "stage": "enter", "from": "travel" },
    ...
  ],
  "cards": {
    "flights":   { "title": "...", "rows": [...] },
    "hotels":    { "title": "...", "rows": [...] },
    "itinerary": { "title": "...", "rows": [...] },
    "reminder":  { "title": "...", "rows": [...] }
  }
}
```

The export step runs as part of `dotnet build` of the test project (target `ExportStoryboards`), writing to the Flutter assets folder. Asset is registered in `pubspec.yaml`.

`DemoRunner.play(storyboardId)` reads the JSON, schedules `Timer`s on `t` offsets, dispatches BLoC events. `DemoRunner.replan()` plays the second scenario without resetting cards. `DemoRunner.replay(cardId)` re-fires that card's authoring arcs.

## Tokens & motion (Dart constants)

```dart
class InoShellTheme {
  static const Color ink0 = Color(0xFF0A0E14);
  static const Color ink1 = Color(0xFF11161F);
  static const Color ink2 = Color(0xFF161D29);
  static const Color line = Color(0x247D8AFF);          // rgba(125,138,255,0.14)
  static const Color lineStrong = Color(0x477D8AFF);    // rgba(125,138,255,0.28)
  static const Color cyan = Color(0xFF3DDCFF);          // neuron
  static const Color indigo = Color(0xFF7C8AFF);        // synapse
  static const Color gold = Color(0xFFE8C56A);          // recall — only warm
  static const Color pink = Color(0xFFF4B8E4);
  static const Color red = Color(0xFFFF6B6B);           // incident — sparingly
  static const Color text = Color(0xFFE6EDF7);
  static const Color muted = Color(0xFF7C8AAA);
  static const Color muted2 = Color(0xFF5A6680);

  static const Cubic easeOut = Cubic(0.22, 1, 0.36, 1);

  static const Duration cometDur = Duration(milliseconds: 480);  // 320–540 range
  static const Duration cardEntryDur = Duration(milliseconds: 240);
  static const Duration brainIdleBeat = Duration(milliseconds: 4800);
  static const double cameraOrbitRadPerSec = 0.05;

  // Latency budget (asserted by demo runner & wired to SLO logging in slice 2)
  static const Duration utteranceToFirstCometBudget = Duration(milliseconds: 400);
  static const Duration toFirstCardBudget = Duration(milliseconds: 2500);
  static const Duration toCompletePlanBudget = Duration(seconds: 6);
}
```

Glass effect: `BackdropFilter(filter: ImageFilter.blur(sigmaX: 24, sigmaY: 24))` + linear-gradient container + 1px hairline border.

## Decay rule

Visual rules locked for slice 1:

1. **Gold = recall**. A comet is gold iff its source neuron is in the recall cluster. Nothing else uses gold.
2. **Brightening on access**. When a recall comet arrives at a target, the inspector decay sparkline for the source briefly spikes upward (mock-only in slice 1; backed by journal in slice 2).
3. **Indigo = compute**. Default comet color.
4. **Cyan = neuron resting**. Default neuron mesh tint when not flared.
5. **Red = incident**. Used only on timeline life marks; never on neurons or comets in normal flow.

## Out of scope

- Live LLM hookup (slice 2 — DemoRunner is replaced by `BrainStreamService`)
- L1 / L2 / L3 self-improvement visualization beyond static life marks on the timeline
- Cross-domain trace filtering UI in the inspector
- Mobile/Telegram surface adaptations of the shell
- Replacing `/brain` (only **adding** `/shell`)
- Backend journaling for decay (mock only)
- Real `traceparent` propagation in the tooltip (synthetic in slice 1)

## Risks / known unknowns

| Risk | Plan |
|---|---|
| `persona_orb.riv` state machine inputs unknown | Inspect via Rive editor or `rive` package introspection; document gap if missing states; map to closest available input |
| `three_js` raycaster on transient comet meshes | Spike: verify `Raycaster.intersectObjects` works against the dynamic comet head list mid-animation. Fallback: pick by 2D screen distance from projected head center |
| Reqnroll JSON exporter | No built-in. Implement as a custom Reqnroll plugin (`IReqnrollPlugin`) or an `[BeforeScenario]/[AfterScenario]` recorder that captures canonical events and emits JSON. Spec the plugin in the impl plan |
| Cluster label projection performance | 8 clusters × 60 fps = 480 reprojections/s — fine. Use `RepaintBoundary` to isolate from the canvas repaint |
| CRLF in storyboard JSON | Force LF in the Reqnroll exporter (`Environment.NewLine` → `"\n"`) and add `*.json text eol=lf` to `.gitattributes` if needed |
| Telegram surface | Out of scope — but verify the shell still renders inside the Telegram WebApp WebView at smaller sizes (graceful degradation; not pixel-perfect) |

## Verification (slice 1 acceptance)

1. `dotnet build ino.slnx` — green
2. `dotnet test ino.slnx` — green (Tokyo.feature scenarios pass against TestCluster)
3. `aspire run` — kernel Healthy in dashboard
4. Open kernel HTTPS URL → navigate to `/shell` in Chrome (via Chrome DevTools MCP)
5. Click "Play demo · Tokyo, 6s" — orb listening → thinking, comets fire from cortex → PlanTrip, then PlanTrip → FindFlights/FindHotels/FindPlaces, recall comet from Preferences arrives gold, three travel cards arc in, reminder comet to VisaReminder, reminder card enters, orb celebrates → idle
6. Click "make day 3 cheaper" — replan plays, hotels card morphs (swap-flash highlight on the swapped row)
7. Click any neuron → inspector drawer opens with last-20 synapses + decay sparkline + (if LlmNeuron) prompt corpus
8. Click a comet head mid-flight → tooltip shows traceparent + decay + payload; click outside → resumes
9. Click tokens button → tokens panel opens; auto-focus toggle changes camera behaviour
10. Aspire Structured Logs filter `ino-flutter` shows BLoC transitions for the demo run; Traces show synthetic spans for each fired comet
11. `/brain` route still works unchanged

## File touches (summary)

Added:
- `clients/ino.flutter/lib/screens/shell/` — 11 new files listed above
- `clients/ino.flutter/lib/screens/shell/demo_runner.dart`
- `clients/ino.flutter/assets/storyboards/tokyo.json` (generated, committed)
- `domains/travel/Ino.Domains.Travel.Tests/Features/Tokyo.feature`
- `domains/travel/Ino.Domains.Travel.Tests/StoryboardExporter/` (Reqnroll plugin or step recorder + JSON serializer)
- `docs/superpowers/specs/2026-05-07-ino-shell-design.md` (this file)

Modified:
- `clients/ino.flutter/lib/app.dart` — register `/shell` GoRoute
- `clients/ino.flutter/pubspec.yaml` — register `assets/storyboards/`
- `clients/ino.flutter/lib/state/timeline_bloc.dart` — add `density`, `lifeMarks` fields
- `clients/ino.flutter/lib/state/brain_inspector_bloc.dart` — add paused-synapse field if absent
- `domains/travel/Ino.Domains.Travel.Tests/Ino.Domains.Travel.Tests.csproj` — Reqnroll deps + `ExportStoryboards` MSBuild target
- `Ino.Kernel.csproj` — wire the storyboard asset folder into the wwwroot copy step (already copies Flutter `build/web/*`; storyboards land under `assets/storyboards/` automatically)

Untouched:
- `lib/screens/brain/*` — `/brain` route unchanged
- `lib/persona/persona_widget.dart` — used as-is
- `iaw/` — substrate untouched
- gRPC layer — untouched in slice 1
