# Project: Living Canvas UI Unification & Simplification Slice 1 (S1)

## Architecture
- **Framework**: Flutter (Dart) with `go_router`, `grpc`, RFW (`rfw`), and liquid-glass UI kit.
- **Goal**: Unify UI into a single `LivingCanvasScreen` that hosts a full-bleed neuron graph (`LiveScreen`) and a floating prompt dock (`FloatingPromptDock`).
- **Clean up**: Remove ~14,400 lines of legacy screens, routing, and orphaned Dart files.
- **Verification**: Zero errors via `flutter analyze`, successful `flutter build web --release`, and E2E tests green via `dotnet test`.

## Milestones
| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| 1 | Baseline & Branch Setup | Git branch creation, baseline Dart file count, and baseline analyzer check. | None | DONE |
| 2 | LivingCanvasScreen & Routing | Create `LivingCanvasScreen` in `UI/flutter/lib/features/canvas/living_canvas_screen.dart`, route `/` to it, and remove legacy routes. | Milestone 1 | DONE |
| 3 | Core Legacy Deletions | Delete `BrainSceneScreen`, the Constellation feature directory, and `ConstructorEditorHomePage`. | Milestone 2 | DONE |
| 4 | Sweep Orphaned Files | Run `flutter analyze`-driven sweep of unused widget, editor, and controller files under `UI/flutter/lib/`. | Milestone 3 | DONE |
| 5 | Verification & Web Build | Run final `flutter analyze`, complete successful web release build, and verify E2E tests with `dotnet test`. | Milestone 4 | DONE |

## Interface Contracts & Component Communication
- `LivingCanvasScreen`: Single entry point for application. Mounts `LiveScreen` (graph view) and `FloatingPromptDock` (query bar).
- `LiveScreenController`: Manages interactions and rendering on the canvas.
- `SynapseStreamFeed` / `SynapseStreamScope`: Manages the synapse event flow to RFW widgets.
- `DigitalBrainClientScope`: Wires the gRPC client dynamically down the widget tree.

## Code Layout
- `UI/flutter/lib/features/canvas/living_canvas_screen.dart`: The new unified screen.
- `UI/flutter/lib/router.dart`: Handles GoRouter routing definitions.
- `UI/flutter/lib/features/live/`: Active graph viewer and card widgets (kept).
- `UI/flutter/lib/features/brain/widgets/floating_prompt_dock.dart`: The floating prompt input bar (kept).
- `UI/flutter/lib/rfw_host/`: Real Declarative UI Host runtime (kept).
