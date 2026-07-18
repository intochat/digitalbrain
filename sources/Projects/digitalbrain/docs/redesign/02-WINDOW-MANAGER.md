# 02 — Window Manager: Panels, Docking, Auto-Layout

Goal: turn each `RfwCardEnvelope` into a draggable, dockable, minimizable panel on
the canvas, replacing the static `Positioned` overlays in
`living_canvas_screen.dart`. Windows-style positioning + a one-click cleanup.

## The model

```
PanelManager (ChangeNotifier / Bloc)
  panels: List<CanvasPanel>          // ordered by z-index
  CanvasPanel {
    id        : String               // RfwCardEnvelope.correlation_id
    title     : String               // caller_neuron_type or surface-declared
    rect      : Rect                  // x, y, w, h in canvas space
    z         : int
    state     : normal | minimized | docked(edge)
    surface   : RfwSurfaceSpec        // library_name, root_widget, data_json
  }
  add(envelope) / remove(id) / raise(id) / move(id, delta) / resize(id, rect)
  autoLayout(preset) / saveLayout() / loadLayout(string)
```

The panel **chrome** (title bar + drag handle + resize grips + minimize/close
buttons) is generic Dart shell, written once. The panel **body** is
`RfwRuntimeHost.render(...)` — unchanged.

## Package choice

Two paradigms; we take a **hybrid**.

### Floating panels — primary (matches "widgets like windows")
[`simple_floating_panel`](https://pub.dev/packages/simple_floating_panel) v2.0.0
— desktop-style draggable + resizable panels, z-order, minimize/restore + dock
UX, separate move/resize handles (avoids gesture conflicts with scrollables
inside an RFW surface). Works on web. This is the closest off-the-shelf match to
the mental model.

### IDE docking — borrow the serialization
[`docking`](https://pub.dev/packages/docking) v1.16.1 (caduandrade) — split +
tabbed regions, drag tabs between areas, and crucially **`stringify`/`load`** to
serialize a layout to/from a String. We use this *concept* (and optionally the
package) for **saved layouts and the auto-layout preset**.

> Decision: start with `simple_floating_panel` for the free-floating feel; adopt
> the `stringify`/`load` layout-string idea (hand-rolled or via `docking`) for
> persistence + auto-layout. Re-evaluate folding fully into `docking` if users
> want true tabbed/split docking later.
>
> **Not** `window_manager` — that manages the *OS* window, not in-canvas widgets.

## Interactions

| Gesture | Result |
|---|---|
| Drag title bar | Move panel (`move`) |
| Drag corner grip | Resize (`resize`, clamped to `PanelConstraints`) |
| Click panel | `raise` to top z |
| Minimize | `state = minimized`; collapses to a dock strip thumbnail |
| Drag to edge | Snap-dock to that edge (`state = docked(edge)`) |
| Close | `remove`; optionally emits a `Neuron.Deactivated` synapse |
| **Auto-layout button** | `autoLayout(grid)` — re-flow to tidy grid + animate viewport |

## Auto-layout

`autoLayout(preset)`:
1. Compute target rects (grid by panel count, or a named saved layout string).
2. Animate each panel `rect` to target (existing `AnimationController` patterns).
3. Emit a `UiViewportSignal` (already in `uigateway.proto`) so the camera/spring
   settles — keeps the canvas background graph in sync.

Presets: `grid` (default cleanup), `focus` (one big + rest minimized to dock),
`saved:<name>` (a serialized layout the user pinned).

## Persistence (per brain)

Layouts are per-brain state (V4-3 isolation): persist the serialized layout
string under the brain's namespace. On load, restore panel geometry; panels whose
neuron is no longer active are dropped (and logged, never silently). Storage rides
the existing grain/data-store path — **no tokens or state to plain files**.

## Acceptance for this layer

- `WatchHomeFeed` subscribed; each `RfwCardEnvelope` → one panel.
- Drag / resize / raise / minimize / close all work on web (`flutter-web`).
- Auto-layout button tidies any arrangement in one animated step.
- Layout survives a reload (persisted + restored).
- Zero per-neuron Dart in the panel system.
