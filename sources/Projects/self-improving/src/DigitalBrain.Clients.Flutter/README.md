# DigitalBrain.Clients.Flutter (project file named Flutter.proj like v1/UI/flutter/Flutter.proj)

This is the canonical Flutter client for the DigitalBrain neuron/synapse OS (final/ reboot). Located at UI/flutter/Flutter.proj to match the v1 structure. Windows client support included.

## How it is started "properly" (the IFlutter contract)

Exactly like `IAspire` lets the brain neuron start/restart distributed apps and per-domain kernels:

- `IFlutter` (Sdk/Microsoft/Flutter/IFlutter.cs) : INeuron + IHandle<StartFlutterClient> + IEmit<FlutterClientStarted>
- Convenient method: `StartFlutterClientAsync(target: "web-server")` — just emits the command synapse (journaled, visible to ino, Creator, timeline, etc.).
- The grain `Flutter : Neuron, IFlutter` (implements the neuron base with journals + lifecycle):
  - When the kernel is running inside a real `DistributedApplication` (i.e. launched by AppHost under `aspire run`): it resolves the DA and drives the "flutter-client" executable resource via `ResourceCommandService` (restart/start). This is the Aspire Flutter integration.
  - In standalone / `dotnet run start.cs` / simulation: best-effort `Process.Start("flutter", "run -d ...")` from this directory.
- The brain (DigitalBrainGrain), ino, REPL, or a Creator proposal can therefore say "bring up the polished UI" using the exact same nervous system as everything else. No snowflake hosting code outside neurons.

## Surface transport

- Live UiSurface / UiWidget trees (alarms, kerneltasks, marketplace listings + install buttons, reviews, etc.) are emitted by their owning neurons (KernelTaskSupervisor, MarketplaceNeuron, SoftwareEngineeringTeam, ...).
- Primary path for all clients: Orleans timeline (UiSurface synapse) + `SurfaceRenderer` (hex1b console).
- Dedicated gRPC path for Flutter (and future rich clients): `SurfaceStreamService` (Kernel) implements the `SurfaceStream` service from `Protos/Surfaces.proto`. Clients call `SubscribeSurfaces` and receive `UiSurfaceMessage { surfaceId, emitter, widgetJson, timestamp }`.
- The Dart side (this project) is responsible for parsing the widgetJson (or a future full proto of the UiWidget union) into real Flutter widgets and wiring Button.OnTap back to the brain (as a synapse or FlutterUiEvent).

See also:
- `Sdk/Microsoft/Flutter/*`
- `Core/Domain/Events/Agentic.cs` (StartFlutter / FlutterUiEvent for back-channel)
- `Kernel/Experiences/SurfaceStreamService.cs`
- `AppHost.cs` (the flutter-client executable resource declaration)
- `docs/USER-FLOWS.md` (the IFlutter start flow)
- The richer reference client lives in `ino/clients/ino.flutter/` (Rive, RFW, persona, full brain topology, voice, scenarios, etc.).

## Running manually (dev)

```bash
cd src/DigitalBrain.Clients.Flutter
flutter pub get
flutter run -d web-server --web-port 8080
```

The IFlutter grain + Aspire resource do the equivalent automatically when you ask the brain to start the client.

## UI Kit parity with hex1b (the important part)

The Flutter client now renders **exactly the same UiWidget union** as the console:

- `lib/ui/ui_widget.dart` contains the Dart mirror + `fromJson` + `buildFromUiWidget(...)`.
- `buildFromUiWidget` is the direct analogue of `SurfaceRenderer.Render(ctx, widget, fire)` in `DigitalBrain.Clients.Console/SurfaceRenderer.cs`.
- Same layout primitives (Column/Row = VStack/HStack, Card with title, Button that carries a real Synapse as OnTap, Text, Markdown via flutter_markdown).
- When you tap a button the `onFire` callback receives the raw OnTap synapse JSON — exactly what `fire(onTap)` does on the hex1b side. The host app turns it into a real `SendAsync` (or routes it as a `FlutterUiEvent` through IFlutter).

Demo surfaces in `main.dart` include real examples emitted by KernelTaskSupervisor, MarketplaceNeuron, SoftwareEngineeringTeam (review Markdown + buttons), alarms, etc.

Switch surfaces with the chips or the FAB. Taps are logged and some produce simulated brain reactions (new surfaces appear) — this is the same mental model as the TUI.

## Real gRPC connection (SurfaceStream)

When IFlutter has started the client under a real Aspire resource:

```dart
final channel = ClientChannel('localhost', port: 8080, ...); // or use Aspire service discovery
final client = SurfaceStreamClient(channel);
final call = client.subscribeSurfaces(SurfaceSubscription());
await for (final msg in call.responseStream) {
  final tree = UiWidget.fromJson(jsonDecode(msg.widgetJson));
  // setState, render with buildFromUiWidget(tree, onFire: sendSynapseToBrain)
}
```

Generate the Dart stubs from `Kernel/Protos/Surfaces.proto` (Stage 2: SurfaceSubscription now has username=2, brain_id=3; plus Login/Add/Archive rpcs for picker).

Exact commands + pins (2026-06 env verified: protoc_plugin 25.0.0 active; protoc binary from protobuf release matching):
  cd src/DigitalBrain.Clients.Flutter
  dart pub global activate protoc_plugin 25.0.0
  # ensure protoc (e.g. 3.25+) on PATH from https://github.com/protocolbuffers/protobuf/releases
  protoc --dart_out=lib/grpc --proto_path ../DigitalBrain.Kernel/Protos ../DigitalBrain.Kernel/Protos/Surfaces.proto
  flutter pub get
  # then dart analyze or flutter build to verify

Keep metadata/header fallback in surface_stream_connection.dart + server for one stage compat.

Taps can be sent back by emitting a `FlutterUiEvent` (or a dedicated client-intent synapse) that IFlutter or a small dispatcher turns into the real inner synapse.

This client is the minimal thing that makes "works with ui kit and renders ui just like hex1b" true while staying inside the neuron/synapse model.

## Requirements for a full interactive client (next steps)

- Proper gRPC web / channel + generated stubs for `SurfaceStream`.
- A real way to deliver the fired OnTap synapse back into the brain (gRPC "send" method, or the existing FlutterUiEvent path through IFlutter).
- (Optional) richer experiences from the ino.flutter tree (Rive, RFW, persona, full brain topology, voice, scenarios, etc.).

The control contract (IFlutter + Aspire resource) + the renderer contract (UiWidget parity) are now solid in the canonical tree.