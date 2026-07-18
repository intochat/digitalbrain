# 01 — Architecture: Substrate & the Two-Tier Seam

## Decision 1 — Keep RFW as the runtime substrate

The Flutter client is already a generic RFW host:

- `rfw_runtime_host.dart` — one process-wide `Runtime`, the `digitalbrain`
  `LocalWidgetLibrary`, per-key parsed document libraries.
- `digitalbrain_rfw_library.dart` — **92 themed primitives**
  (`createDigitalBrainWidgets()`), every one reading `Theme.of(context)` /
  `DigitalBrainColors` so server-emitted UI inherits the visual language for free.
- `event_table.dart` / `RemoteEventHandler` — RFW `event "name" {...}` →
  app-side capability → synapse fire back to the kernel.
- Wire: `digitalbrain.proto` `RfwCardEnvelope{ library_name, root_widget,
  data_json }` over `WatchHomeFeed` / `GetRfwLayout`.

### Why not Widgetbook, Stac, or json_dynamic_widget

| Candidate | Verdict |
|---|---|
| **Widgetbook** | Dev-time catalog only (Storybook-style). Cannot render server-driven/AI UI in a shipped app. **Keep it — but as the design-time catalog of our palette**, not the renderer. |
| **Stac** | Real SDUI engine, JSON-based, larger default catalog. But adopting it throws away the 92-widget dictionary + theme work and *still* needs a Dart rebuild to add a primitive. **Use only as a checklist** of primitives worth adding. |
| **json_dynamic_widget** | Same trade as Stac, smaller ecosystem. No. |
| **RFW (current)** | Flutter-team maintained, compact binary over gRPC, already integrated. **Keep.** |

**Invariant W-3 stands:** RFW is the engine; Widgetbook catalogs the palette;
Stac is a reference list.

## Decision 2 — The two-tier model (the rebuild seam)

Everything splits along the user's hard constraint: *"if I need to rebuild
binaries, I restart the Aspire container."* Put the seam exactly there.

```
┌──────────────────────────── TIER 1 — PALETTE ────────────────────────────┐
│ Dart widgets registered in the RFW LocalWidgetLibrary                     │
│ (digitalbrain_rfw_library.dart). Adding one = BINARY REBUILD.            │
│ Rare, batched, human-initiated. Examples to add:                         │
│   LottiePlayer · AnalogClock · CountdownClock · EarthGlobe · FloatingWindow│
│ ── rebuild line: aspire stop → aspire start (flutter-web resource) ──────│
└───────────────────────────────────────────────────────────────────────────┘
┌──────────────────────────── TIER 2 — LAYOUTS ────────────────────────────┐
│ .ino `rfw:` blocks composing the palette. Shipped over gRPC. NO REBUILD. │
│ Constant, AI/user-authored. This is where every intent-driven widget,    │
│ every user dashboard, every downloaded-domain surface lives.             │
└───────────────────────────────────────────────────────────────────────────┘
```

### The rule the Creator/AI must follow

- **Composing** an existing primitive into a new arrangement → Tier 2, no rebuild.
  (A countdown panel when `CountdownClock` exists = just data.)
- **Inventing** a genuinely new primitive render → Tier 1, escalate to a human
  rebuild. (A brand-new "3D ECG trace" widget = new Dart in the dictionary.)

This keeps rebuilds rare and predictable, and makes "users create their own
layouts (AI-generated UI)" a zero-rebuild, everyday operation.

## Decision 3 — A widget is a positioned RFW surface

The window-manager (see `02-WINDOW-MANAGER.md`) does **not** introduce a parallel
UI system. A "widget/window" is:

```
Panel {
  id            // correlation id from RfwCardEnvelope
  surface       // RemoteWidget(runtime, library_name, root_widget, data_json)
  geometry      // x, y, w, h, z   (client-managed, persisted per brain)
  state         // normal | minimized | docked
}
```

The surface is rendered by the existing `RfwRuntimeHost.render(...)`. The panel
chrome (title bar, drag handle, resize grips, minimize) is the *only* new Dart
shell — and it is generic, not per-neuron.

## Decision 4 — Reuse the proto that already exists

- `WatchHomeFeed` (stream `RfwCardEnvelope`) is defined in `digitalbrain.proto`
  but **not yet subscribed** in the client. Wiring it = each card spawns a panel.
- `uigateway.proto` already defines `UiViewportSignal` (camera/spring) and layout
  enums (`CARD/PANEL/MODAL/INLINE/CANVAS`) — **spec'd, unwired.** The auto-layout
  action emits a `UiViewportSignal`.

No new proto is required for slice 1; we light up what's already declared.

## What this does NOT change

- The `.ino` one-file-per-neuron invariant (V5-1).
- The single-route shell (`router.dart`, one `/`).
- The theme, the 92 existing primitives, the event dispatch.
- The kernel being a pure gRPC/Orleans host (no Flutter serving).
