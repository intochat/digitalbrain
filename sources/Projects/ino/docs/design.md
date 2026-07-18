# ino — UI / design direction

ino's mobile client is a persona-forward surface: a friendly mascot that
lives on every screen, and three peer views that show the system from
three angles — spatial, active, chronological. Every pixel traces back to
a real neural event; nothing is decorative.

This doc is the single source of truth for client UI. The underlying
runtime (domains, neurons, synapses) is covered in
[`vision.md`](./vision.md) and
[`neuron-unified-vision.md`](./neuron-unified-vision.md).

## Principles

1. **Synapse-as-message.** A synapse carries a payload between neurons;
   the UI renders that payload *as* the visible element. A Gmail →
   Notifier arc literally draws the text `"Sarah · PR #1204 ready"` as it
   travels. The animation IS the notification; there is no separate
   notification layer.
2. **Same data, three lenses.** Mind, Live, and Trace project the same
   neural event stream into three affordances. No duplicate state, no
   separate feeds.
3. **Persona always present.** A small cartoon mascot sits bottom-right
   on every screen — the launcher for Settings / Marketplace /
   Domains / Neurons / Synapses / Memory. Tap opens a glassmorphic
   drawer.
4. **Two themes, one identity.** Dark (`#05030f` cosmos) and light
   (`#faf7ff` editorial). Amber `#ff9f43` is the ino identity in both —
   it MUST be preserved as the accent across palette flips.

## Three core views

| View | Role | What it shows |
|---|---|---|
| **Mind** | Spatial · passive | A living constellation of the user's installed domain neurons as glowing spheres. Synapse arcs fire between them with floating message labels. The camera gently follows the active neuron. |
| **Live** | Active · interactive | Contextual cockpit. Each currently-running neuron flow surfaces its own Remote Flutter Widget card. User can swipe between cards and tap verb buttons directly (`AddStop`, `Skip`, `Cancel`, `Reply`) without going through voice. |
| **Trace** | Chronological · historical | Scrollable log of events. Each row = app icon + event sentence + synapse signature (e.g. `mime: write → send`). Same data as Mind, different lens. |

Bottom tab bar: `Mind · Live · Trace`. Live sits in the middle because
it's the most-tapped surface when anything is happening.

## Persona launcher

Bottom-right floating mascot, 64px circular, amber halo + live dot. Tap
opens a glassmorphic bottom sheet:

- Hero: larger mascot + "Hey, {user}" + "ino listening · {N} events today"
- 2×3 tile grid: **Settings** · **Marketplace** · **Domains** ·
  **Neurons** · **Synapses** · **Memory**

The mascot is the only entry to system-level surfaces — it keeps the
three core views un-cluttered.

## Prototypes

Historical HTML prototypes that shaped the current direction live at
[`POC/docs/prototypes/`](../POC/docs/prototypes/):

- `01-taxi-flow.html` — end-to-end taxi flow: speech → persona
  mime → RFW live card → iOS notification. Established the
  synapse-as-message principle and the verb → mime vocabulary.
- `02-domain-catalog.html` — top-100 apps mapped into ~15 shared
  persona mimes. Motivates the domain manifest shape: `entry synapse →
  mime → notification hook`.

## Active mockups

High-fidelity Stitch project: **`ino · persona + mind + trace`**
(Stitch project id `17844203391936990908`). Four screens × two themes:

- Mind (dark, light)
- Live (dark; light pending)
- Trace (dark, light)
- Launcher drawer (dark, light)

Designs exported as Flutter widget code via Stitch's native Flutter
export + MCP bridge — drops directly into
[`clients/ino.flutter/lib/ui/`](../clients/ino.flutter/lib/ui/). Stitch
is a prototyping surface; the source of truth for shipped UI is the
Flutter code.

## Persona implementation

The mascot renders through two paths, selected at runtime:

- **Rive** (primary) — `.riv` asset at
  [`clients/ino.flutter/assets/rive/persona_orb.riv`](../clients/ino.flutter/assets/rive/).
  State machine contract documented at
  [`clients/ino.flutter/assets/rive/README.md`](../clients/ino.flutter/assets/rive/README.md):
  inputs `mood`, `energy`, `pulse` plus per-activity triggers
  (`trigger_searching_flights`, `trigger_thinking`, `trigger_idle`, …).
- **CustomPaint fallback** — `lib/persona/persona_widget.dart` ships the
  procedural orb that renders if the `.riv` asset is missing, invalid,
  or the state machine contract isn't met.

Reference asset for mascot energy:
[rive.app/marketplace · 011y](https://rive.app/marketplace/26076-48718-011y/)
(the observability-adjacent AI mascot; similar expression range and
friendliness we want).

## Relationship to `SceneGraph`

Per [`CLAUDE.md`](../CLAUDE.md): neurons emit `ViewState`,
`SceneComposerAgent` builds a `SceneGraph`, renderers translate to
specific surfaces. Mind / Live / Trace are three `SceneGraph` shapes
consumed by the Flutter renderer. The same `SceneGraph` powers the
Telegram mini-app renderer (Telegram webview reuses `ino.flutter`) and,
eventually, a terminal renderer. The views are surface-agnostic; only
the renderer changes.

## Themes

Palette lives in the Stitch project's design system "Aetheric Dark /
Light". Tokens that MUST be consistent across both themes:

| Role | Dark | Light |
|---|---|---|
| Background | `#05030f` → `#1a0d3d` gradient | `#faf7ff` → `#ece6f7` gradient |
| Body text | `#e8e4ff` | `#1a0d3d` |
| Secondary text | `#9a93c2` | `#6b6190` |
| Accent (identity) | `#ff9f43` | `#ff9f43` (unchanged) |
| Synapse arc | `#ff9f43` (glowing) | `#e8822b` (deeper) |
| Synapse chip | `#1a0f06` bg + amber text | `#fff4e6` bg + amber text |
| Surface (frosted) | `#1d1929` at 90% | `#ffffff` at 85% |

Type: **Space Grotesk** for headlines (tight tracking, -0.02em to
-0.04em), **Inter** for body, **SF Mono / JetBrains Mono** for synapse
signatures.

## What not to build

- No separate notification surface — synapses *are* the notifications.
- No per-flow settings drawer — domains and neurons declare their
  capabilities; the `Settings` tile surfaces them
  centrally.
- No "home screen" beyond the three views + persona launcher. If a
  surface doesn't fit Mind / Live / Trace, it's a drawer destination.

## Status

| Piece | Status |
|---|---|
| Prototype HTMLs | ✅ landed (`POC/docs/prototypes/`) |
| Stitch mockups — dark | ✅ four screens generated |
| Stitch mockups — light | ⚠️ three screens done; Live light pending |
| Rive persona contract | ✅ documented; `.riv` asset not yet committed |
| Flutter `PersonaWidget` | ✅ CustomPaint fallback live |
| Flutter Mind / Live / Trace screens | ❌ not started — Stitch export target |
| `SceneGraph` for mobile views | ❌ not started |
