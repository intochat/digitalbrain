# Handoff Report: Living Canvas UI Unification & Simplification Slice 1 (S1)

This handoff report summarizes the read-only exploration and analysis of visual widgets, editor directories, and core system dependencies in DigitalBrain, providing a safe transition and actionable plan for S1 implementation.

---

## 1. Observation
We conducted project-wide directory scans and targeted text search operations in the `UI/flutter/` codebase:

- **Visual Widget/Editor Files**: Using `find_by_name` in `UI/flutter/lib/features` and `UI/flutter/lib/widgets`, we discovered:
  - **46 Dart files** in `features/` and **8 Dart files** in `widgets/`.
  - Directory structures include `features/brain/`, `features/canvas/`, `features/constellation/`, `features/home/`, `features/ino_editor/`, `features/live/`, and `features/neuron_constructor/`.
  
- **Orphan Candidate Imports**: Running `grep_search` across `UI/flutter/lib/` for files under feature folders yielded exact usages:
  - `leader_line_painter.dart`: **0 results** found.
  - `mini_brain_badge.dart`: **0 results** found.
  - `brain_models.dart`: Imported only by `features/brain/brain_scene_screen.dart` (Line 36).
  - `active_neurons_panel.dart`: Imported only by `features/brain/brain_scene_screen.dart` (Line 39).
  - `card_surfaces.dart`: Imported only by `features/brain/brain_scene_screen.dart` (Line 37).
  - `editor_body.dart`: Imported only by `features/brain/brain_scene_screen.dart` (Line 38).
  - `editor_card_source.dart`: Imported only by `features/brain/widgets/editor_body.dart` (Line 4).
  - `ino_syntax_highlight_controller.dart`: Imported only by `features/home/constructor_editor_home_page.dart` (Line 14).
  - `constellation` directory: All files (`brain_camera.dart`, `brain_mesh.dart`, `brain_node_widget.dart`, `comparative_harness_widget.dart`, `constellation_screen.dart`) are imported exclusively in `router.dart` and `brain_scene_screen.dart`.
  
- **Core Dependencies (Must Keep)**:
  - `LiveScreen` (`UI/flutter/lib/features/live/live_screen.dart`) imports and coordinates **18 files** inside `features/live/` (including cards, layout engines, comet painters, search bars, and timeline rings).
  - `digital_brain_ui/` contains **17 files** constituting the liquid-glass kit, including `glass/liquid_glass_surface.dart`, `effects/brain_scene_effects.dart`, and `glow/glow_icon.dart`.
  - `rfw_host/` contains **7 files** (RFW Declarative UI Host runtime).
  - `grpc/` contains **14 files** (Orleans protobuf client endpoint and connection layers).
  - `telemetry/` contains **10 files** (OTLP metric/log exporters).
  
- **Routing Configurations**:
  - `UI/flutter/lib/router.dart` registers `/constellation` (`ConstellationScreen`), `/` (`ConstructorEditorHomePage`), and `/brain/:brainId` (`BrainSceneScreen`).
  - `UI/flutter/lib/features/canvas/living_canvas_screen.dart` is constructed but has **no inbound references** (0 imports found). It is a standalone unified canvas screen container.

---

## 2. Logic Chain
1. **Observation 1**: `/` in `router.dart` currently points to `ConstructorEditorHomePage`. The target architecture in `PROJECT.md` specifies that the root route `/` must point to `LivingCanvasScreen` in S1.
2. **Inference 1**: Therefore, `router.dart` must be modified to import `LivingCanvasScreen`, map `/` to it, and remove mapping for legacy screens.
3. **Observation 2**: `BrainSceneScreen`, `ConstellationScreen`, and `ConstructorEditorHomePage` are legacy entry points. The target architecture specifies their complete removal.
4. **Inference 2**: Therefore, these three files can be deleted. This will break any local dependencies.
5. **Observation 3**: The secondary files (`brain_models.dart`, `active_neurons_panel.dart`, `card_surfaces.dart`, `editor_body.dart`, `editor_card_source.dart`, `ino_syntax_highlight_controller.dart`, and the constellation folder files) are imported ONLY by the deleted legacy screens.
6. **Inference 3**: Therefore, deleting the legacy screens will cause these secondary files to have exactly zero inbound imports, making them completely orphaned.
7. **Observation 4**: `leader_line_painter.dart` and `mini_brain_badge.dart` already have zero imports.
8. **Inference 4**: Therefore, they are currently orphaned and can be deleted immediately.
9. **Observation 5**: Core modules like `LiveScreen`, the `digital_brain_ui` liquid-glass toolkit, `rfw_host`, `grpc`, and `telemetry` are heavily used by the active runtime and the new `LivingCanvasScreen`.
10. **Inference 5**: Therefore, they are outside the sweep boundary and must be preserved intact.

---

## 3. Caveats
- **Offline / Mock Fallbacks**: Standalone Flutter runs without a running Orleans backend resolved via the `kernel` environment variables will fall back to mock data. This is handled gracefully by both `LiveScreen` and the RFW layer and must remain supported.
- **RFW Runtime Envelopes**: While clean-up is aggressive, the switch-case card registry in `widgets/digitalbrain_widgets.dart` lists card keys served by Orleans. If any swept widget keys are requested dynamically by the backend in a custom Orleans grain spec, a runtime warning will occur. Thus, `digitalbrain_widgets.dart` must keep its card mapping blocks intact unless backend schema changes are coordinated.

---

## 4. Conclusion
We have established a clear, watertight boundary for the Slice 1 UI simplification sweep.
1. The **Living Canvas UI boundary** must preserve `features/live/`, `digital_brain_ui/`, `rfw_host/`, `grpc/`, `telemetry/`, `shell/`, `theme/`, and `widgets/` directories.
2. **12 Dart files** and the entire `constellation` directory can be safely deleted.
3. A precise, safe step-by-step sweep strategy (Phase 1: S1 Setup -> Phase 2: Screen Deletions -> Phase 3: Secondary Orphan Sweep -> Phase 4: Verification) minimizes build failures.

---

## 5. Verification Method
To verify that the sweep is complete and successful, subsequent implementers must run:

1. **Static Compilation Analysis**:
   ```powershell
   cd UI/flutter
   flutter analyze
   ```
   *Expected outcome*: Zero errors, zero warnings, zero unresolved imports.
2. **Web Release Build Verification**:
   ```powershell
   flutter build web --release
   ```
   *Expected outcome*: Successful build compilation and package generation.
3. **E2E Integration Verification**:
   ```powershell
   dotnet test
   ```
   *Expected outcome*: All integration specs and backend test assertions execute green.
