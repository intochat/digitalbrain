# ino.flutter

Flutter client for ino — renders the three core views (Mind · Live ·
Trace) plus the persona launcher on top of the runtime's `SceneGraph`.

## UI direction

See [`docs/design.md`](../../docs/design.md) (repo root) for the full
spec: view anatomy, persona contract, themes, Stitch mockup project, and
the Flutter export path.

Stitch project (high-fidelity mockups, exportable to Flutter widget
code): **`ino · persona + mind + trace`** — id `17844203391936990908`.

## Persona

The floating mascot uses Rive when a `.riv` asset is available and falls
back to a procedural `CustomPaint` orb otherwise. State-machine contract
(inputs `mood`, `energy`, `pulse`, per-activity triggers) is documented
at [`assets/rive/README.md`](assets/rive/README.md). Fallback renderer
lives at `lib/persona/persona_widget.dart`.

## Getting started

Standard Flutter project layout — `flutter pub get` then
`flutter run -d chrome` for web (CanvasKit) or `flutter run` for a
connected device.

Learning resources (upstream Flutter docs):
- [Learn Flutter](https://docs.flutter.dev/get-started/learn-flutter)
- [Flutter API reference](https://api.flutter.dev/)
