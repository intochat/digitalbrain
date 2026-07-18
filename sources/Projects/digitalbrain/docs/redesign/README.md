# Redesign: The Widget Canvas

> Plan for the next DigitalBrain shell: **multiple widgets instead of multiple
> windows.** A single full-bleed canvas where every neuron surfaces as a
> draggable, dockable mini-app, composed at runtime from a fixed widget palette.
>
> Read order: this index → `00-VISION` → `01-ARCHITECTURE` → the rest.
> Status: **planning** (no code written yet). Supersedes nothing; extends the
> v5/v6 "UI is data" invariant (V5-4) with a window-manager layer.

## The one-paragraph summary

We already have the right substrate: the Flutter client is a production-grade
**RFW (Remote Flutter Widgets)** host — a 92-widget themed dictionary, gRPC card
delivery (`RfwCardEnvelope`), and event dispatch back to the kernel. We do **not**
adopt Widgetbook as a runtime (it is a dev-time catalog only) and we do **not**
switch to Stac/json_dynamic_widget (RFW already does the job and is Flutter-team
maintained). What's missing are two layers on top: a **window-manager** (drag /
dock / auto-layout floating panels) and a richer **widget palette** (Lottie,
analog clock, countdown, earth globe). Both fit cleanly on either side of the
"rebuild binaries → restart Aspire" line.

## The redesign invariants (don't relitigate)

- **W-1 One canvas, many widgets.** The shell is one full-bleed scene. Each
  neuron's RFW surface is a free-floating, dockable **panel** — not a route,
  not an OS window. This replaces the static `Positioned` overlays in
  `living_canvas_screen.dart`.
- **W-2 Two tiers, one seam.** *Palette* primitives are Dart widgets in the RFW
  dictionary — adding one needs a **binary rebuild** (rare, batched). *Layouts*
  are `.ino` `rfw:` blocks composed from the palette and shipped over gRPC —
  **no rebuild** (constant, AI/user-authored). The rebuild/restart line is the
  Tier-1 ⇄ Tier-2 boundary.
- **W-3 Keep RFW.** RFW is the runtime substrate. Widgetbook is the *design-time
  catalog* of the palette. Stac is a reference checklist of primitives to add,
  not a replacement engine.
- **W-4 Intent makes widgets.** "Set a clock" / "remind me in 10m" / "show
  flight BA286" each activate a neuron that emits an `RfwCardEnvelope`; the
  canvas spawns a panel. No Dart written per intent — pure Tier-2.
- **W-5 The mess is recoverable.** Panels drag freely; an **auto-layout** action
  re-flows them to a tidy preset (serialized layout string) and emits a
  viewport signal. Layouts persist per brain.

## File map

| File | What it covers |
|---|---|
| `00-VISION.md` | The widget-canvas product vision; what the user sees |
| `01-ARCHITECTURE.md` | RFW substrate decision; the two-tier model; the rebuild seam |
| `02-WINDOW-MANAGER.md` | Floating panels, docking, z-order, auto-layout, persistence |
| `03-WIDGET-PALETTE.md` | Tier-1 primitives: Lottie, AnalogClock, CountdownClock, EarthGlobe, FloatingWindow |
| `04-INTENT-FLOW.md` | intent → neuron → card → panel; clock / reminder / flight worked examples |
| `05-ROADMAP.md` | Build order in slices; the one thing to de-risk first |
| `06-PACKAGES.md` | Package shortlist, versions, web/wasm risks, sources |

## Grounding (real paths this plan touches)

- Renderer: `UI/flutter/lib/rfw_host/rfw_runtime_host.dart`
- Palette: `UI/flutter/lib/rfw_host/digitalbrain_rfw_library.dart` (92 widgets)
- Shell screen: `UI/flutter/lib/features/canvas/living_canvas_screen.dart`
- Wire contract: `kernel/DigitalBrain.Runtime/Protos/digitalbrain.proto`
  (`WatchHomeFeed`, `RfwCardEnvelope`, `GetRfwLayout`) and `uigateway.proto`
  (`UiViewportSignal`, layout enums) — the latter is spec'd but unwired.
