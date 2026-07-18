# 00 — Vision: The Widget Canvas

## The shift

The old desktop metaphor: **many windows**. Our metaphor: **many widgets on one
canvas**. Where a legacy OS would open a Clock window, a Weather window, a Flight
Tracker window, DigitalBrain shows a clock widget, a weather widget, a globe
widget — all living on the same full-bleed brain scene, each backed by a neuron,
each draggable and dockable, all sharing one theme and one event bus.

This is the natural endpoint of **V5-4 (UI is data)**: if every neuron already
declares its surface as RFW, then a "window" is just a neuron's surface given a
position, a z-index, and a drag handle.

## What the user sees

1. **A single scene.** No tab bar, no route stack. One canvas (`/`), full-bleed,
   dark, cinematic — the existing `LivingCanvasScreen` evolved.
2. **Widgets appear on intent.** The user says (voice or text) "set a clock" — an
   analog clock panel fades in. "Remind me in 10 minutes" — a countdown clock
   panel appears, hands ticking backward. "Show flight BA286" — a globe panel
   spins up with an animated origin→destination arc.
3. **Widgets behave like windows — better.** Drag anywhere; resize from a corner;
   minimize to a dock strip; raise/lower z-order by clicking; snap to edges.
4. **One button cleans the mess.** An **auto-layout** control re-flows every
   panel into a tidy grid (or a saved layout), with a smooth viewport animation.
5. **Anyone can compose.** Because layouts are data (`.ino` `rfw:` blocks),
   users — and the AI Creator — can invent new widget arrangements at runtime
   without anyone shipping a new Flutter build.

## Why this is the right shape

- **It reuses what works.** The RFW host, the 92-widget dictionary, the gRPC
  card stream, and the theme are done. This redesign is mostly *shell plumbing
  plus a handful of new primitives* — not an architecture rewrite.
- **It honors the rebuild constraint.** New *layouts* never need a rebuild. Only
  new *primitives* do, and those are batched and infrequent (see
  `01-ARCHITECTURE.md`).
- **It scales to user-generated UI.** The marketplace/domain model (V5-5: domains
  are repos of `.ino` files) means a downloaded domain can ship its own widget
  surfaces — they render on the same canvas with zero client changes, as long as
  they compose from the shared palette.

## Non-goals (for this redesign)

- **Not** real OS windows (`window_manager` manages the *outer* app window only;
  our widgets live *inside* the canvas).
- **Not** a new server-driven-UI engine — RFW stays (W-3).
- **Not** per-neuron Dart widgets — that violates V5-4 and is on the cut list.
- **Not** Widgetbook at runtime — it is a design-time catalog only.

## The three demo widgets (the acceptance bar)

The redesign is "done enough to feel real" when these three intents each spawn a
live, themed, draggable panel with no per-intent Dart:

| Intent | Neuron | Panel | New primitive(s) |
|---|---|---|---|
| "set a clock" | `ClockNeuron` | analog clock | `AnalogClock` |
| "remind me in 10 min" | `ReminderNeuron` | countdown, hands run backward, pulse + Lottie on fire | `CountdownClock`, `LottiePlayer` |
| "show flight BA286" | `FlightNeuron` | 3D globe with animated route arc | `EarthGlobe` |

Each neuron is a single `.ino` file (V5-1) with an `rfw:` block — see
`04-INTENT-FLOW.md`.
