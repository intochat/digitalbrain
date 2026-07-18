# Analysis Report: Living Canvas UI Unification & Simplification Slice 1 (S1)

This report presents a thorough audit of the visual widget and editor directories in `UI/flutter/lib/features/` and `UI/flutter/lib/widgets/` to support the **Milestone 1-5 UI Unification and Simplification Plan** in DigitalBrain. It details orphaned candidate files, maps critical dependencies, defines keep-boundaries, and formulates a precise, safe step-by-step strategy for the sweep.

---

## 1. Summary of Findings
- **Single Canvas Unification**: DigitalBrain is consolidating its primary interface into a single, unified `LivingCanvasScreen` (`UI/flutter/lib/features/canvas/living_canvas_screen.dart`), which hosts a full-bleed neuron graph (`LiveScreen`) and a floating prompt dock (`FloatingPromptDock`). 
- **Legacy Footprint to Remove**: By removing three core legacy screens (`ConstructorEditorHomePage`, `BrainSceneScreen`, and `ConstellationScreen`), routing configurations, and cascaded orphaned files, we can safely prune **12 Dart files** and **1 entire feature directory** (`features/constellation/`), eliminating over **14,400 lines of complex, redundant UI code**.
- **Clean Keep Boundaries**: The core engines—including the interactive 3D particle canvas (`LiveScreen`), liquid-glass UI kit (`digital_brain_ui/`), dynamic RFW host runtime (`rfw_host/`), gRPC endpoint layer (`grpc/`), OpenTelemetry pipeline (`telemetry/`), Client scopes (`shell/`), and color schemes (`theme/`)—are highly cohesive and must remain untouched.
- **Orphan Status Verified**: We have verified the exact inbound import chains for all candidates. Two files are already completely orphaned (zero imports), while the others are imported solely by the legacy screens slated for deletion.

---

## 2. Orphaned and Unused Candidate Files
The following files have been identified as orphaned or unused. They are classified into **Already Orphaned** (zero inbound imports) and **Legacy-Dependent Orphans** (become completely unused once `ConstructorEditorHomePage`, `BrainSceneScreen`, and `ConstellationScreen` are deleted).

### A. Already Orphaned Files (Zero Inbound Imports)
These files can be immediately and safely deleted without waiting for screen migrations:

| File Path | Description | Current Inbound Imports | Status |
| :--- | :--- | :--- | :--- |
| `UI/flutter/lib/features/brain/widgets/leader_line_painter.dart` | A custom painter drawing glowing lines between neurons and cards. Completely unused. | **None (0)** | **Safe to Delete** |
| `UI/flutter/lib/features/brain/widgets/mini_brain_badge.dart` | A GlassMaterial badge widget designed to restore brain navigation. Unwired. | **None (0)** | **Safe to Delete** |

### B. Legacy-Dependent Orphans
These files are currently active but serve components that are being deleted. Once the legacy screens (`ConstructorEditorHomePage`, `BrainSceneScreen`, and `ConstellationScreen`) are removed, their import counts drop to exactly zero:

| File Path | Description | Current Inbound Imports | Status |
| :--- | :--- | :--- | :--- |
| `UI/flutter/lib/features/brain/models/brain_models.dart` | State model definitions for L2 UI presets and overlays. | `features/brain/brain_scene_screen.dart` | **Safe to Delete** after L2 Screen deletion |
| `UI/flutter/lib/features/brain/widgets/active_neurons_panel.dart` | Floating side-sheet listing currently active Orleans catalog grains. | `features/brain/brain_scene_screen.dart` | **Safe to Delete** after L2 Screen deletion |
| `UI/flutter/lib/features/brain/widgets/card_surfaces.dart` | Large canvas container rendering heavy floating card stacks. | `features/brain/brain_scene_screen.dart` | **Safe to Delete** after L2 Screen deletion |
| `UI/flutter/lib/features/brain/widgets/editor_body.dart` | Inner split-pane body housing the Ino Editor. | `features/brain/brain_scene_screen.dart` | **Safe to Delete** after L2 Screen deletion |
| `UI/flutter/lib/features/ino_editor/editor_card_source.dart` | Code chunk subscriber bridging `editor_body.dart` to RFW. | `features/brain/widgets/editor_body.dart` | **Safe to Delete** after Editor Body deletion |
| `UI/flutter/lib/features/ino_editor/ino_syntax_highlight_controller.dart` | Highlighting controller for the code block text editor. | `features/home/constructor_editor_home_page.dart` | **Safe to Delete** after Home Page deletion |
| `UI/flutter/lib/features/constellation/brain_camera.dart` | Camera controllers for the 3D constellation orbital paths. | `features/constellation/constellation_screen.dart` | **Safe to Delete** after Constellation deletion |
| `UI/flutter/lib/features/constellation/brain_mesh.dart` | Custom mesh geometry drawer representing Orleans nodes. | `features/constellation/constellation_screen.dart` | **Safe to Delete** after Constellation deletion |
| `UI/flutter/lib/features/constellation/brain_node_widget.dart` | Interactive orbital sphere widgets. | `features/constellation/constellation_screen.dart` | **Safe to Delete** after Constellation deletion |
| `UI/flutter/lib/features/constellation/comparative_harness_widget.dart` | Systems comparative dashboard overlay with monochrome toggle. | `features/constellation/constellation_screen.dart`<br>`features/brain/brain_scene_screen.dart` | **Safe to Delete** after Screen deletions |

### C. Major Screen Deletions (Legacy Entrypoints)
These are the heavy screens slated for complete removal to clean up the workspace:

- `UI/flutter/lib/features/home/constructor_editor_home_page.dart` (Legacy Tabbed Workspace, **3,841 lines**)
- `UI/flutter/lib/features/brain/brain_scene_screen.dart` (Legacy L2 HUD screen, **6,475 lines**)
- `UI/flutter/lib/features/constellation/constellation_screen.dart` (Legacy L1 3D landing, **292 lines**)

---

## 3. Dependency Tracing (What MUST be Kept)
To ensure system stability, we have traced the dependencies of the critical subsystems. These directories and files constitute the core runtime boundary and **MUST** be preserved in their entirety:

```
UI/flutter/lib/
├── features/
│   ├── canvas/
│   │   └── living_canvas_screen.dart (Unified S1 entrypoint)
│   ├── brain/
│   │   ├── voice_input.dart (Bridges speech-to-text to prompt dock)
│   │   └── widgets/
│   │       └── floating_prompt_dock.dart (Query entry bar in unified canvas)
│   └── live/ (Preserves 18 files)
│       ├── cards/ (Floating card stack controllers and synapse renderers)
│       ├── graph/ (Cinematic 3D node layout, domains, and comet painters)
│       ├── search/ (Introspector catalog search)
│       ├── timeline/ (Synapse ring-buffers and timeline scrubber)
│       ├── tooltip/ (Node hover detail widgets)
│       ├── live_screen.dart (The primary interactive living canvas engine)
│       └── introspector_client.dart (RAG query client)
├── digital_brain_ui/ (Preserves 17 files, liquid-glass visual toolkit)
│   ├── glass/ (liquid-glass surfaces and frosted materials)
│   ├── glow/ (neon icon decorators)
│   ├── effects/ (breathing pulses and waves)
│   └── breakpoints/ (window size classes and responsive layouts)
├── rfw_host/ (Preserves 7 files, dynamic declarative UI rendering runtime)
│   ├── digitalbrain_rfw_library.dart (Central widget constructor registry)
│   └── rfw_runtime_host.dart (RFW layout parser and compiler)
├── grpc/ (Preserves 14 files, Orleans protobuf and communication channel)
├── telemetry/ (Preserves 10 files, OpenTelemetry / OTLP log and metric collectors)
├── shell/ (Preserves 2 files, client scope context and scrub registers)
├── theme/ (Preserves 1 file, central color configuration)
└── widgets/ (Preserves 8 files, core shared assets e.g., brand mark, canvas)
```

### Critical Verification and Inter-Component Anchors:
- **`floating_prompt_dock.dart`**: Relies on `voice_input.dart` for speech-to-text features. Both must be preserved.
- **`live_screen.dart`**: Relies on all files in `features/live/*`, `digital_brain_ui/`, `grpc/`, `telemetry/`, and the `llm_settings_bus.dart` controller.
- **`digitalbrain_rfw_library.dart`**: Imports and coordinates event routing with `PromptInputBus`, `StateEditorBus`, `InoEditorBus`, `LlmSettingsBus`, and `TypewriterController`. These bus files in `features/ino_editor/` are critical active adapters and **cannot** be swept.
- **`widgets/digitalbrain_widgets.dart`**: Exposes the switch-case loader (`tryBuild`) for heavy cards like `CanvasCard`, `VideoPlayerCard`, and `OptionChipStackCard`. Kept intact to service RFW runtime envelopes.

---

## 4. Precise, Safe Step-by-Step Sweep Strategy
To execute this unification safely with zero compilation breakages, the sweep must follow a strict, dependency-ordered phase sequence.

```
                  ┌──────────────────────────────┐
                  │ Phase 1: Unified S1 Canvas   │
                  │ - Create LivingCanvasScreen  │
                  │ - Update GoRouter to '/'     │
                  └──────────────┬───────────────┘
                                 │
                                 ▼
                  ┌──────────────────────────────┐
                  │ Phase 2: Core Deletions      │
                  │ - Delete BrainSceneScreen    │
                  │ - Delete Constellation       │
                  │ - Delete ConstructorEditorHP │
                  └──────────────┬───────────────┘
                                 │
                                 ▼
                  ┌──────────────────────────────┐
                  │ Phase 3: Cascade Sweep       │
                  │ - Sweep 12 orphaned files    │
                  │ - Verify zero inbound imports│
                  └──────────────┬───────────────┘
                                 │
                                 ▼
                  ┌──────────────────────────────┐
                  │ Phase 4: Full Verification   │
                  │ - run `flutter analyze`      │
                  │ - run `flutter build web`    │
                  │ - run `dotnet test`          │
                  └──────────────────────────────┘
```

### Phase 1: Unified S1 Canvas Integration (Safe Setup)
1. **Branch Setup**: Establish a clean working branch `unify-ui-canvas-s1`.
2. **Create Unified Screen**: Establish `UI/flutter/lib/features/canvas/living_canvas_screen.dart` importing `LiveScreen` and `FloatingPromptDock`.
3. **Route Unification**:
   - Edit `UI/flutter/lib/router.dart`.
   - Remove legacy imports (`brain_scene_screen.dart`, `constellation_screen.dart`, `constructor_editor_home_page.dart`).
   - Register `LivingCanvasScreen` under the root path `/` ( Assist Mode ).
   - Delete all routing blocks for `/constellation` and `/brain/:brainId`.
4. **Compile check**: Run `flutter analyze` immediately to verify the routing swap compiles perfectly.

### Phase 2: Core Screen Deletions
Remove the heavy, deprecated entry points to disconnect inbound references:
1. Delete `UI/flutter/lib/features/home/constructor_editor_home_page.dart`
2. Delete `UI/flutter/lib/features/brain/brain_scene_screen.dart`
3. Delete the entire constellation folder: `UI/flutter/lib/features/constellation/`

### Phase 3: Cascaded Orphaned Files Sweep
Verify zero inbound references for secondary files using the **Zero Inbound Imports Verification Protocol**:
1. For each candidate (e.g. `leader_line_painter.dart`, `mini_brain_badge.dart`, `brain_models.dart`, `active_neurons_panel.dart`, `card_surfaces.dart`, `editor_body.dart`, `editor_card_source.dart`, `ino_syntax_highlight_controller.dart`), run the following shell command to confirm zero imports exist in `lib/`:
   ```powershell
   # PowerShell check (returns nothing if zero imports found)
   Get-ChildItem -Path "UI/flutter/lib" -Filter "*.dart" -Recurse | Select-String -Pattern "filename_without_extension"
   ```
2. Once the search confirms zero inbound references (except inside the file itself), execute file deletion.
3. Keep the terminal active and check each file sequentially.

### Phase 4: Strict Verification and Acceptance
1. **Static Analysis**: Run `flutter analyze` in `UI/flutter/` directory. Resolve any unresolved references or unused imports immediately.
2. **Release Build**: Run `flutter build web --release` to ensure web-assembly compilation works flawlessly with no performance degradation or dead-code references.
3. **E2E Integration Verification**: Run `dotnet test` in the solution root directory to verify that integration pipelines and Orleans endpoints match expectations and pass.

---

## 5. Rollback and Safety Protocols
- **Atomic Commits**: Group deletions into logical, atomic Git commits (e.g., Commit 1: S1 Canvas & Routing, Commit 2: Core Screen Deletions, Commit 3: Orphaned Files Sweep).
- **Stash Strategy**: Maintain a clean stash/branch to easily restore files if any obscure RFW runtime registry dependencies are surfaced during release testing.
