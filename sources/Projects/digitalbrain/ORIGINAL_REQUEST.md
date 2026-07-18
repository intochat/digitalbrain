# Original User Request

## Initial Request — 2026-05-27T15:07:30Z

Ruthlessly debug the blank/white home screen issue in the Flutter UI, continue codebase simplification, and remake the Flutter UI under the premium Ino Constructor and Ino Code Editor vision.

Working directory: `e:\digitalbrain`
Integrity mode: `development`

## Requirements

### R1. Diagnose & Fix the Blank UI Screen
- Investigate and resolve the blank/white screen on load.
- Avoid external `GoogleFonts` downloads if running in an offline environment (fall back to safe system sans-serif fonts).
- Ensure no null/unhandled exceptions on cold activation of `BrainCanvas` or `NeuronConstructorView`.

### R2. Remake UI with Ino Constructor & Ino Code Editor Vision
- **Visual Neuron Constructor (Left Pane):** Implement a pure custom interactive visual node-based editor representing Neurons and Synapses using `GestureDetector`s, `CustomPainter` for connections, and custom state (keeping it robust, lightweight, and zero external dependency). Users must be able to click-to-spawn nodes and drag lines to connect them.
- **Ino Code Editor (Right Pane):** Simulated/real syntax highlighting for InoLang keywords (`neuron`, `synapse`, `on`, `emit`, `ask`, `scenario`, `given`, `when`, `then`), dynamically synchronized with selected nodes.
- **Brain Canvas Chat & Visualizer (Bottom/Overlay):** Dynamic, animated 2D neural graph canvas drawing when the user types "visualize [concept]".
- **Stunning Navigation:** Floating HUD button "3D Constellation View" that transitions smoothly to `/brain/digitalbrain`.

### R3. Continuous Codebase Simplification
- Audit `UI/flutter/lib/` and delete stale, deprecated, or duplicate widgets/controllers.

## Acceptance Criteria

### UI Functional Verification
- [ ] No blank/white screen on cold boot; page renders instantly and gracefully.
- [ ] Visual Constructor successfully allows interactive node creation and line dragging.
- [ ] Ino Code Editor displays syntax-highlighted code that syncs with selected nodes.
- [ ] Typing "visualize" in the chat area animates a custom drawn canvas representation.
- [ ] Solution compiles with zero errors across C# and Flutter builds.

### Verification Mechanism
- [ ] Playwright E2E browser checks or standard Flutter integration tests confirm clean UI loading and basic click/drag capability without throwing unhandled exceptions.

## Follow-up — 2026-05-27T18:15:41+02:00

Unify the InoLang runtime by simplifying compilation and execution, enabling live dynamic Orleans grain registration, and supporting bidirectional hot-reload synchronization with the UI and file saves.

Working directory: e:\digitalbrain
Integrity mode: development

## Requirements

### R1. Ruthless AST and Compiler Simplification
- Simplify `DigitalBrain.InoLang` by pruning redundant parsers, legacy tokenizers, and obsolete AST definitions while keeping the language spec simple and declarative.
- Compile `.ino` declarations directly into Roslyn-executable C# scripts containing the neuron's behavior, signals, routing rules, and UI blocks.

### R2. Live Compiler & Orleans Dynamic Grain Registration
- Implement dynamic, reflection-less grain registration within Orleans. 
- When a new `neuron` is declared or saved in a `.ino` file, Orleans must dynamically activate a generic `DynamicNeuronGrain` executing the lowered Roslyn C# script.
- Support behavioral keywords (`ask`, `emit`, `on`):
  - `ask` to invoke downstream/SDK/AI neurons dynamically.
  - `emit` to route synapses to connected targets.
  - `on` to trigger logic when incoming matching synapses are received.

### R3. Bidirectional Hot-Reload & UI Synchronization
- Connect the C# backend with the UI Visual Constructor and Ino Code Editor.
- Hot-reload active Orleans neuron topologies and recompilation must trigger automatically on both file-system saves (via directory watcher) and UI visual edits.
- Feed real-time activation logs or results to the Chat visualizer pane when a synapse fires.

### R4. Test Coverage & Clean Sweeps
- Verify all implementations with automated tests, keeping the entire suite (`dotnet test`) green.
- Prune obsolete boilerplate, redundant grain interfaces, and unused routing catalog tables across the codebase.

## Acceptance Criteria

### InoLang Parser & Code Generator
- [ ] Obsolete compiler, parser, and lexer code is cleaned up and deleted from `DigitalBrain.InoLang`.
- [ ] Compiler successfully generates clean Roslyn C# script strings from `.ino` definitions.

### Dynamic Orleans Runtime
- [ ] Saving an `.ino` file compiles it and dynamically registers and activates the neuron in the Orleans cluster without Silo restarts.
- [ ] Active neurons route synapses (`emit` and `ask` signals) dynamically between themselves based on InoLang rules.

### Synchronization & Hot-Reload
- [ ] Backend monitors `.ino` files for changes and automatically recompiles and hot-reloads the active Orleans grain topology.
- [ ] dragging new visual connections in the UI successfully triggers recompilation and hot-reload.

### Project Health
- [ ] All C# and Orleans tests in the solution compile and pass cleanly via `dotnet test`.

## Follow-up — 2026-05-29T23:08:44Z

Implement the Living Canvas UI Unification & Simplification Slice 1 (S1) in DigitalBrain, which replaces legacy screens (~14,400 lines) with one clean, unified canvas and sweeps the orphaned files.

Working directory: E:\digitalbrain
Integrity mode: development

## Requirements

### R1. Implement LivingCanvasScreen
Create the new `LivingCanvasScreen` in `UI/flutter/lib/features/canvas/living_canvas_screen.dart` using a full-bleed `LiveScreen` graph widget and a `FloatingPromptDock` as specified in [2026-05-29-flutter-cut-living-canvas-s1.md](file:///E:/digitalbrain/docs/superpowers/plans/2026-05-29-flutter-cut-living-canvas-s1.md).

### R2. Re-Route Root to LivingCanvasScreen
Modify `UI/flutter/lib/router.dart` to mount `LivingCanvasScreen` as the root (`/`) route and remove legacy `/constellation` and `/brain/:brainId` routes, as well as the unused `BrainScenePlaceholder`.

### R3. Delete Legacy Screens and Features
Delete the following files and directories cleanly:
- `UI/flutter/lib/features/brain/brain_scene_screen.dart`
- `UI/flutter/lib/features/constellation/` (entire directory)
- `UI/flutter/lib/features/home/constructor_editor_home_page.dart`
- `UI/flutter/lib/features/neuron_constructor/neuron_constructor_view.dart`
- `UI/flutter/lib/features/neuron_constructor/liquid_glass_3d_brain.dart`

### R4. Sweep Orphaned/Unused Files
Identify and sweep all remaining unused files inside `UI/flutter/lib/` using `flutter analyze` diagnostics to ensure clean deletion of dead code.

### R5. Verify Build and Test Suite
Ensure that the Flutter web release builds successfully and the entire C# backend/E2E test suite remains perfectly green via `dotnet test`.

## Acceptance Criteria

### Compilation & Diagnostics
- Running `flutter analyze` inside `UI/flutter` finishes with no errors or warnings introduced by this cut.
- Running `flutter build web --release` completes successfully with zero compile-time errors.

### Code Reduction
- The total number of `.dart` files in `UI/flutter/lib/` is significantly reduced from the baseline of 120 (measured at Step 1).
- Over 14,000 lines of legacy screen code are deleted.

### Backend Integrity
- Running `dotnet test` from `E:\digitalbrain` passes successfully, verifying that all E2E gRPC and RFW contracts remain fully intact.
