# digitalbrain_flutter

Northbound pixel host for DigitalBrain (architecture §4.6).

## Status

- **Built (this package):** pure Dart edge client + projection of `SceneOpenedEvent` → host view model; dual golden lives in sibling `digitalbrain_wire`.
- **Designed:** Flutter Windows desktop chrome (`flutter run -d windows`) when a Flutter SDK is on PATH. Do not embed Orleans or talk MCP tools as the UI bus.

## Edge

```
Dart host  --HTTP/SSE-->  hosts/DigitalBrain.Ui  --IDigitalBrain-->  silo + FlutterModule
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
