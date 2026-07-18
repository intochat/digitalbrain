# 03 — Widget Palette (Tier-1 Primitives)

These are the new Dart primitives to register in
`digitalbrain_rfw_library.dart` via `createDigitalBrainWidgets()`. Adding them is
**one batched binary rebuild** (Tier 1). After that, every primitive is
composable from `.ino` `rfw:` blocks with **no further rebuilds** (Tier 2).

Each primitive: reads `Theme.of(context)` / `DigitalBrainColors` (theme-inherited,
W-3 consistency), takes all variable inputs from RFW `data`, fires user actions as
RFW `event`s. Add a Widgetbook use-case per primitive (design-time catalog).

## FloatingWindow
The panel frame primitive (if not handled purely by the shell). Props:
`title`, `lockState` (idle/busy/modal, honoring V4-5), `child`. Mostly the shell
draws chrome; this exists so a surface can *declare* its preferred frame.

## LottiePlayer
Package: [`lottie`](https://pub.dev/packages/lottie) — pure Dart, web (wasm) +
all desktop. Props from RFW data:

| prop | type | meaning |
|---|---|---|
| `src` | string | asset key or network URL (`Lottie.asset`/`.network`) |
| `loop` | bool | repeat |
| `autoplay` | bool | start on mount |
| `speed` | double | playback rate |

Drives a custom `AnimationController` for play/seek/reverse. Used standalone and
as the "reminder fired" celebration overlay. Web: prefer CanvasKit renderer.

## AnalogClock
Custom `CustomPainter` (you already have `brain_painter.dart` / `canvas_3d.dart`
expertise). Optionally the [`flutter_analog_clock`](https://pub.dev/packages/flutter_analog_clock)
package. Props:

| prop | type | meaning |
|---|---|---|
| `tz` | string | IANA timezone (default local) |
| `showSeconds` | bool | render second hand |
| `face` | string | `minimal` \| `numerals` |

Self-driving (a 1s ticker); no data round-trips needed for the tick.

## CountdownClock
The reminder primitive — same painter family as `AnalogClock` but **hands run
backward** toward zero. Props:

| prop | type | meaning |
|---|---|---|
| `durationSeconds` | int | total countdown |
| `startedAtUtc` | string | server start time (drift-free remaining = duration − (now − start)) |
| `onZeroEvent` | string | RFW event name fired locally at zero (pulse + Lottie) |

At zero: pulse the panel, play a `LottiePlayer` celebration, and the owning
`ReminderNeuron` independently fires its reminder synapse (the UI reaction and the
domain reaction are decoupled — UI is data).

## EarthGlobe
Package: [`flutter_earth_globe`](https://github.com/Pana-g/flutter_earth_globe)
v2.1.0 — GPU/shader 3D globe; auto-rotation; animated point markers; **animated
connection arcs** (grow-in + dashed travel) — ideal for a flight route. Props:

| prop | type | meaning |
|---|---|---|
| `points` | list | `[{lat, lng, label}]` markers |
| `arcs` | list | `[{from:{lat,lng}, to:{lat,lng}, style}]` connections |
| `autoRotate` | bool | spin |

Maps to `controller.addPoint(...)` / `controller.addPointConnection(...)`.

> ⚠️ **Web risk (de-risk first):** the globe's web path requires
> `flutter build web --wasm`. Confirm it coexists with the current
> `flutter run -d web-server --release` resource and with Lottie's preferred
> CanvasKit renderer **before** committing the primitive. See `06-PACKAGES.md`.

## Palette summary

| Primitive | Package / approach | Tier | Web note |
|---|---|---|---|
| FloatingWindow | shell Dart | 1 | — |
| LottiePlayer | `lottie` | 1 | CanvasKit/wasm |
| AnalogClock | CustomPainter / `flutter_analog_clock` | 1 | ok |
| CountdownClock | CustomPainter (backward) | 1 | ok |
| EarthGlobe | `flutter_earth_globe` 2.1.0 | 1 | needs `--wasm` |

After this single rebuild, the three demo neurons (`04-INTENT-FLOW.md`) and any
future widget are pure Tier-2 data.
