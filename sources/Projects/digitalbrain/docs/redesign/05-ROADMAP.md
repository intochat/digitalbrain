# 05 — Roadmap (Slices)

Build order. Each slice is independently demoable. Slice 0 first — it removes the
only real unknown.

## Slice 0 — De-risk the web renderer (do this FIRST)
The one genuine risk: `flutter_earth_globe` web needs `flutter build web --wasm`,
while the current `flutter-web` resource runs `flutter run -d web-server
--release`, and Lottie prefers the CanvasKit renderer. **Spike all three together
in a throwaway page** and confirm they coexist on `http://localhost:5800`.
- ✅ Globe renders on web → keep `EarthGlobe` in slice 2.
- ❌ Conflict → globe becomes desktop-only (`flutter-windows`) or deferred; Lottie
  + clocks still ship. Decide before writing the primitive.

## Slice 1 — Window-manager layer (no new primitives)
Replace static `Positioned` cards with a `PanelManager` + `simple_floating_panel`.
- Subscribe `WatchHomeFeed` (already in `digitalbrain.proto`, currently unwired).
- Each `RfwCardEnvelope` → one draggable/resizable/minimizable panel.
- Add the **auto-layout** button → grid re-flow + `UiViewportSignal`.
- Persist/restore layout per brain (serialized layout string).
- **No rebuild-gated work** beyond adding the panel package.

## Slice 2 — Palette expansion (ONE batched rebuild)
Add to `digitalbrain_rfw_library.dart`: `LottiePlayer`, `AnalogClock`,
`CountdownClock`, `EarthGlobe` (if slice 0 passed), `FloatingWindow`.
- One Widgetbook use-case per primitive.
- One `aspire stop` → `aspire start` of `flutter-web`. This is the *only* planned
  rebuild in the whole redesign.

## Slice 3 — The three demo neurons (Tier-2, no rebuild)
`ClockNeuron`, `ReminderNeuron`, `FlightNeuron` — each a single `.ino` with an
`rfw:` block (`04-INTENT-FLOW.md`). Proves intent → widget → panel end-to-end and
the event round-trip (snooze).

## Slice 4 — Layout persistence & presets
Saved layouts (`stringify`/`load`), `focus`/`grid`/`saved:<name>` presets, dock
strip for minimized panels. Optionally evaluate folding into `docking` for true
tabbed/split regions.

## Definition of done
- Three intents each spawn a live, themed, draggable panel — **zero per-intent
  Dart**.
- Auto-layout tidies any mess in one animated step.
- Layout survives reload.
- Exactly one binary rebuild was required across the whole effort (slice 2).
- `dotnet test` green; Aspire integration green; verified once on the running
  `flutter-web` (batch UI changes, verify once — don't loop per tweak).

## Out of scope (later)
True tabbed/split IDE docking, multi-monitor, per-widget marketplace packaging,
collaborative/shared canvases.
