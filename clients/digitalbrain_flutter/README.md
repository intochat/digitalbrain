# digitalbrain_flutter

Northbound OS surface host for DigitalBrain (architecture §4.6).

## Status

- **Built:** pure Dart edge client, SSE frame parse, `ShellSurfaceController` projection,
  headless host `bin/digitalbrain_host.dart` (stdout scene list without restart).
- **Built (module hosting):** AppHost `WithFlutterHost` Auto mode uses Flutter desktop when CLI is
  on PATH, otherwise headless `dart run bin/digitalbrain_host.dart` with `DIGITALBRAIN_UI_BASE`.
- **Designed:** Flutter Windows widget chrome (full `windows/` embedder polish).

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

Optional Windows when Flutter SDK is present:

```bash
flutter run -d windows
# AppHost injects DIGITALBRAIN_UI_BASE into the executable resource
```

## Gates

```bash
# from repo root
dart pub get --directory clients/digitalbrain_wire
dart test --directory clients/digitalbrain_wire
dart pub get --directory clients/digitalbrain_flutter
dart test --directory clients/digitalbrain_flutter
dart analyze clients/digitalbrain_wire clients/digitalbrain_flutter
```

Domain truth remains the root `dotnet test` solution gate. Dart jobs never sole-own shell/scene semantics.
