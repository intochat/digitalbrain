# digitalbrain_flutter

Northbound OS surface host for DigitalBrain (architecture §4.6).

## Status

- **Built:** pure Dart edge client, SSE parse, `ShellSurfaceController` projection,
  headless host `bin/digitalbrain_host.dart`.
- **Built (module hosting):** AppHost `WithFlutterHost` injects `DIGITALBRAIN_UI_BASE` +
  `DIGITALBRAIN_SHELL` only (no Orleans/journal/MCP env). Headless entry is the honest live
  path for this package.
- **Designed:** Flutter Windows widget chrome. This package is **not** a Flutter app yet —
  no `lib/main.dart`, no `windows/` embedder. Do not claim `flutter run` succeeds here.

Do not embed Orleans or talk MCP tools as the UI bus.

## Edge

```
Dart host  --HTTP/SSE-->  hosts/DigitalBrain.Ui  --IDigitalBrain-->  silo + FlutterModule
```

## Headless live host

```bash
# AppHost must be running (module hosting starts digitalbrain-ui)
export DIGITALBRAIN_UI_BASE=http://localhost:<ui-port>   # or set by Aspire
export DIGITALBRAIN_SHELL=desk

cd clients/digitalbrain_flutter
dart run bin/digitalbrain_host.dart --open home:Home
# prints scene-opened lines as SSE projects SceneOpened without restart
```

## Gates

```bash
# from each package directory
dart pub get
dart test
# from repo root
dart analyze clients/digitalbrain_wire clients/digitalbrain_flutter
```

Domain truth remains the root `dotnet test` solution gate. Dart jobs never sole-own shell/scene semantics.
