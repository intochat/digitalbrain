# digitalbrain_flutter

Northbound OS surface host for DigitalBrain (architecture §4.6).

## Status

- **Built:** pure Dart edge client, SSE parse, `ShellSurfaceController` projection,
  headless host `bin/digitalbrain_host.dart`.
- **Built (module hosting):** AppHost `WithFlutterHost` injects `DIGITALBRAIN_UI_BASE` +
  `DIGITALBRAIN_SHELL` only (no Orleans/journal/MCP env).
- **Built (Windows chrome):** `lib/main.dart` + `windows/` Material shell lists scenes by
  key/title from `ShellSurfaceController` / SSE `SceneOpened` only. Host mode **Auto**
  selects Flutter desktop when these markers exist and the Flutter CLI is available;
  otherwise headless.

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
dart run bin/digitalbrain_host.dart --open home:Home
# prints scene-opened lines as SSE projects SceneOpened without restart
```

## Windows desktop host

```bash
export DIGITALBRAIN_UI_BASE=http://localhost:<ui-port>
export DIGITALBRAIN_SHELL=desk

cd clients/digitalbrain_flutter
flutter run -d windows
# or: flutter build windows
```

## Gates

```bash
cd clients/digitalbrain_flutter
flutter pub get
flutter analyze
flutter test
flutter build windows
# dual golden (wire package)
cd ../digitalbrain_wire && dart test
```

Domain truth remains the root `dotnet test` solution gate. Dart jobs never sole-own shell/scene semantics.
