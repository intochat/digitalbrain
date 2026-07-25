# digitalbrain_flutter

Northbound OS surface host for DigitalBrain (architecture §4.6).

## Status

- **Built:** pure Dart edge client, SSE parse (`SseSceneOpenedParser` only),
  `ShellSurfaceController` projection, headless host `bin/digitalbrain_host.dart`.
- **Built (module hosting):** AppHost `WithFlutterHost` injects `DIGITALBRAIN_UI_BASE` +
  `DIGITALBRAIN_SHELL` only (no Orleans/journal/MCP env).
- **Built (Windows chrome):** nested `shell/` Flutter package (`lib/main.dart` + `windows/`)
  Material list of scene key/title from `ShellSurfaceController` / SSE `SceneOpened`.
- **Headless vs desktop (explicit):** root is pure Dart — `WithFlutterHost<HeadlessHost>()` →
  `dart run bin/digitalbrain_host.dart`. Default `WithFlutterHost()` / `<DesktopHost>` →
  `flutter run -d windows` under `shell/`. No Auto fallback.

Do not embed Orleans or talk MCP tools as the UI bus.

## Edge

```
Dart/Flutter host  --HTTP/SSE-->  hosts/DigitalBrain.Ui  --IDigitalBrain-->  silo + FlutterModule
```

## Headless live host

```bash
# AppHost must be running (module hosting starts digitalbrain-ui)
export DIGITALBRAIN_UI_BASE=http://localhost:<ui-port>   # or set by Aspire
export DIGITALBRAIN_SHELL=desk

cd clients/digitalbrain_flutter
dart pub get
dart run bin/digitalbrain_host.dart --open home:Home
# prints scene-opened lines; reconnects on SSE end/error without process exit
```

## Windows desktop host

```bash
export DIGITALBRAIN_UI_BASE=http://localhost:<ui-port>
export DIGITALBRAIN_SHELL=desk

cd clients/digitalbrain_flutter/shell
flutter pub get
flutter run -d windows
# or: flutter build windows
```

## Gates

```bash
cd clients/digitalbrain_flutter
dart pub get
dart analyze
dart test

cd shell
flutter pub get
flutter analyze
flutter test
# when Windows toolchain present:
flutter build windows
# dual golden (wire package)
cd ../../digitalbrain_wire && dart test
```

Domain truth remains the root `dotnet test` solution gate. Dart jobs never sole-own shell/scene semantics.
