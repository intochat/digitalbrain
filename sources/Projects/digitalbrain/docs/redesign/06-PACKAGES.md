# 06 — Packages, Versions & Risks

Verify every API via Context7 / official docs before writing code (project rule).
Use latest versions at implementation time; the versions below are research-time
references.

## Shortlist

| Need | Package | Ref version | Web | Role |
|---|---|---|---|---|
| Runtime SDUI substrate | `rfw` | ^1.0.0 (in repo) | ✅ | **Keep** — the engine |
| Design-time catalog | `widgetbook` | latest | ✅ | Palette workbench (dev only) |
| Lottie | `lottie` | latest | ✅ wasm/CanvasKit | Animations, reminder celebration |
| Earth globe | `flutter_earth_globe` | 2.1.0 | ⚠️ `--wasm` | Flight route arcs |
| Analog/countdown clock | `flutter_analog_clock` or custom `CustomPainter` | latest | ✅ | Clock + backward countdown |
| Floating windows | `simple_floating_panel` | 2.0.0 | ✅ | Drag/resize/z/dock/minimize |
| Docking + layout strings | `docking` | 1.16.1 | ✅ | `stringify`/`load`, auto-layout presets |

Already in `pubspec.yaml` and reusable: `vector_math`, `google_fonts`, the
`glass_refract.frag` shader, `media_kit` (video), custom-paint patterns in
`brain_painter.dart` / `canvas_3d.dart`.

## Risks & mitigations

1. **Globe web build (`--wasm`)** — highest risk. The current `flutter-web`
   resource is `run -d web-server --release`. Spike in Slice 0. Fallback: globe
   on `flutter-windows` only, or defer; clocks + Lottie are unaffected.
2. **Renderer conflict (wasm vs CanvasKit)** — Lottie favors CanvasKit; the globe
   wants wasm. Confirm a single renderer choice serves both, or gate the globe.
3. **Gesture conflicts** — RFW surfaces contain scrollables/buttons; panel
   drag/resize must use *dedicated handles* (`simple_floating_panel` does this),
   not whole-panel gesture capture.
4. **Package maintenance** — `flutter_earth_globe` is small (49★). Pin the
   version; wrap behind the `EarthGlobe` primitive so a future swap is local to
   one Dart file (Tier-1 isolation).
5. **Rebuild discipline** — only Slice 2 rebuilds. If a new primitive is
   tempting mid-stream, batch it into the next palette rebuild rather than
   rebuilding per widget.

## Why not (rejected)

- **Widgetbook as runtime** — dev-time only; cannot render server/AI UI in a
  shipped app.
- **Stac / json_dynamic_widget** — would replace RFW for no capability gain and
  discard the 92-widget dictionary + theme work; both still rebuild for new
  primitives.
- **`window_manager`** — manages the OS window, not in-canvas widgets. Wrong
  layer (though usable separately for the outer app frame).

## Sources
- Widgetbook — https://www.widgetbook.io/
- RFW / Stac / json_dynamic_widget — https://fluttergems.dev/widget-generation-rendering/
- Stac — https://stac.dev/
- lottie — https://pub.dev/packages/lottie
- flutter_earth_globe — https://github.com/Pana-g/flutter_earth_globe
- flutter_analog_clock — https://pub.dev/packages/flutter_analog_clock
- simple_floating_panel — https://pub.dev/packages/simple_floating_panel
- docking — https://pub.dev/packages/docking
- window_manager — https://pub.dev/packages/window_manager
