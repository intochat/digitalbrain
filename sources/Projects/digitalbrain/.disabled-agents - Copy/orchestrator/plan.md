# Implementation Plan - Living Canvas UI Unification & Simplification Slice 1 (S1)

This plan replaces the three legacy Flutter screen routes with a single `LivingCanvasScreen` and cleanly sweeps all orphaned/unused Dart files inside `UI/flutter/lib/`.

## Milestones

### Milestone 1: Baseline & Branch Setup
- **Objective**: Establish the feat branch and capture base diagnostics.
- **Tasks**:
  1. Create the working branch `feat/flutter-cut-living-canvas-s1`.
  2. Measure baseline Dart file count in `UI/flutter/lib/` (target: 120 files).
  3. Run `flutter analyze` to establish the baseline warnings and verify there are no pre-existing compiler errors.

### Milestone 2: LivingCanvasScreen & Routing
- **Objective**: Implement the unified canvas layout and update application routing.
- **Tasks**:
  1. Create `UI/flutter/lib/features/canvas/living_canvas_screen.dart` hosting full-bleed `LiveScreen` neuron graph, floating prompt dock `FloatingPromptDock`, RFW runtime host `RfwRuntimeHost`, and gRPC kernel channels.
  2. Modify `UI/flutter/lib/router.dart` to add temporary `/canvas` route for verification.
  3. Route `/` (root) directly to `LivingCanvasScreen` and remove legacy `/constellation`, `/brain/:brainId` routes, and the `BrainScenePlaceholder`.
  4. Run `flutter analyze` to verify the routes and the canvas compile clean.

### Milestone 3: Core Legacy Deletions
- **Objective**: Remove the heavyweight legacy screen modules and features.
- **Tasks**:
  1. Verify zero imports for `BrainSceneScreen` and delete `UI/flutter/lib/features/brain/brain_scene_screen.dart`.
  2. Delete `UI/flutter/lib/features/constellation/` directory entirely (5 files: screen, camera, mesh, node widget, comparative harness).
  3. Delete `UI/flutter/lib/features/home/constructor_editor_home_page.dart`.
  4. Delete legacy INO-coupled constructor view `UI/flutter/lib/features/neuron_constructor/neuron_constructor_view.dart` and `UI/flutter/lib/features/neuron_constructor/liquid_glass_3d_brain.dart` while preserving the small models (`visual_constructor_models.dart` and `visual_constructor_state.dart`).

### Milestone 4: Sweep Orphaned Files
- **Objective**: Run analyze-driven sweep to aggressively delete dead code.
- **Tasks**:
  1. Run `flutter analyze` and identify all `unused_import`, `unused_element`, and dead code warnings.
  2. For each warning, check for any inbound imports outside its directory.
  3. Delete confirmed-orphaned widget, editor, and helper files in batch (e.g. `ino_editor` files, stale 3D brain/canvas widgets, etc.).
  4. Iteratively repeat analyze & prune cycles until no unused code/warnings remain.

### Milestone 5: Verification & Web Build
- **Objective**: Ensure high-fidelity operational verification.
- **Tasks**:
  1. Run final `flutter analyze` ensuring `No issues found!`.
  2. Run `flutter build web --release` ensuring a clean production bundle.
  3. Rebuild the Aspire `flutter-web` resource and verify UI loading/interaction.
  4. Run E2E tests via `dotnet test` from the workspace root ensuring the C# backend remains perfectly green.
  5. Measure final file reduction and commit delta.

---

## Verification Gates
1. `flutter analyze` reports ZERO errors or warnings.
2. `flutter build web --release` completes successfully.
3. `dotnet test` reports all E2E C# tests green.
4. Forensic Auditor reports CLEAN.
