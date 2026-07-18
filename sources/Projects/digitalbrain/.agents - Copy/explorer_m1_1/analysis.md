# Living Canvas UI Unification & Simplification Slice 1 (S1) — Baseline & Branch Setup Analysis

## Executive Summary
This analysis report establishes the baseline environment state, repository status, Dart source file counts, and analyzer diagnostics for the first slice (S1) of the Flutter Living Canvas UI Unification.
All targets and keepers identified in the implementation plan (`docs/superpowers/plans/2026-05-29-flutter-cut-living-canvas-s1.md`) have been verified as physically present. The environment is on the correct feature branch (`feat/flutter-cut-living-canvas-s1`) and contains zero compiler errors, confirming a clean, high-confidence starting point for implementer execution.

---

## 1. Git Status & Branch Verification
* **Current Working Branch**: `feat/flutter-cut-living-canvas-s1` (verified active and checked out).
* **Git Cleanliness**: The repository is clean regarding the core codebase, with the only uncommitted changes located inside the `.agents/` metadata directory and `ORIGINAL_REQUEST.md`.
* **Branch Existence**: Checked out from `master`, already fully set up for Slice 1 development.

---

## 2. Baseline Dart File Count in UI/flutter
To ensure absolute accuracy, measurements were conducted using multiple glob patterns from the `UI/flutter/` directory:

1. **Strict Nested Pattern (`git ls-files "lib/**/*.dart"`)**:
   * **Count**: `105` files.
   * *Note*: This pattern excludes the three top-level Dart files directly in `lib/` due to standard Windows/PowerShell glob parsing behavior.
2. **Inclusive lib Pattern (`git ls-files "lib/*.dart" "lib/**/*.dart"`)**:
   * **Count**: `108` files.
   * *Details*: This is the exact physical count of tracked Dart source files under `lib/`.
3. **Workspace Tracked Pattern (`git ls-files | grep "\.dart$"`)**:
   * **Count**: `112` files.
   * *Details*: Includes `lib/` files plus 4 tool/stress-test files (`tool/` folder).

### Base File Count Recommendation
For the downstream implementer's file reduction tracking (Milestone 1 to Milestone 5 comparison), the baseline is **108** files within `lib/`.
The target reduction after all cuts is approximately 20+ files.

---

## 3. Baseline `flutter analyze` Diagnostics
The baseline analyzer run in `UI/flutter/` returned **150 issues found**. Crucially, **there are ZERO compile errors** (`error` status).

### Diagnostics Summary
* **Errors**: `0`
* **Warnings**: `24` in `lib/` (mostly unused variables, unused imports, or unused elements in legacy screens) + `5` in `tool/` stress tests.
* **Info Messages**: `121` (mostly stylistic or deprecations such as `curly_braces_in_flow_control_structures`, `deprecated_member_use`, `avoid_print`, or `withOpacity`).

### Verbatim Warnings in `lib/` (Pre-existing Baseline)
These warnings serve as the "orphaned import/variable map" for subsequent dead-code sweeps:
1. `lib\digital_brain_ui\glass\glass_material.dart:46:8` — `unused_field` (`_isHovered`)
2. `lib\digital_brain_ui\glass\glass_material.dart:95:11` — `unused_local_variable` (`effectiveSigma`)
3. `lib\digital_brain_ui\glass\glass_material.dart:141:20` — `dead_code`
4. `lib\features\brain\widgets\card_surfaces.dart:3:8` — `unused_import` (`digital_brain_ui.dart`)
5. `lib\features\brain\widgets\card_surfaces.dart:8:8` — `unused_import` (`digitalbrain_widgets.dart`)
6. `lib\features\brain\widgets\floating_prompt_dock.dart:8:8` — `unused_import` (`digital_brain_ui.dart`)
7. `lib\features\canvas\living_canvas_screen.dart:24:24` — `unused_field` (`_host`)
8. `lib\features\canvas\living_canvas_screen.dart:32:8` — `unused_field` (`_voiceActive`)
9. `lib\features\constellation\constellation_screen.dart:8:8` — `unused_import` (`grpc_or_grpcweb.dart`)
10. `lib\features\constellation\constellation_screen.dart:2579:22` — `unused_local_variable` (`rate`)
11. `lib\features\home\constructor_editor_home_page.dart:47:8` — `unused_field` (`_isVisualizing`)
12. `lib\features\home\constructor_editor_home_page.dart:48:8` — `unused_field` (`_isNavigating`)
13. `lib\features\home\constructor_editor_home_page.dart:56:37` — `unused_field` (`_canvasKey`)
14. `lib\features\home\constructor_editor_home_page.dart:89:8` — `unused_field` (`_hasActivatedHook`)
15. `lib\features\home\constructor_editor_home_page.dart:90:8` — `unused_field` (`_hasDeactivatedHook`)
16. `lib\features\home\constructor_editor_home_page.dart:92:10` — `unused_field` (`_telemetryLogs`)
17. `lib\features\home\constructor_editor_home_page.dart:477:16` — `unused_element` (`_startCompilationStream`)
18. `lib\features\home\constructor_editor_home_page.dart:483:11` — `invalid_null_aware_operator`
19. `lib\features\home\constructor_editor_home_page.dart:976:16` — `unused_element` (`_saveAndHotReloadActiveNeuron`)
20. `lib\features\home\constructor_editor_home_page.dart:1741:10` — `unused_element` (`_buildLlmControllerPanel`)
21. `lib\features\home\constructor_editor_home_page.dart:2063:10` — `unused_element` (`_buildChatColumn`)
22. `lib\features\neuron_constructor\liquid_glass_3d_brain.dart:6:8` — `unused_import` (`glass_material.dart`)
23. `lib\features\neuron_constructor\neuron_constructor_view.dart:2241:10` — `unused_element` (`_buildAutopilotDock`)
24. `lib\features\neuron_constructor\neuron_constructor_view.dart:2306:10` — `unused_element` (`_buildBddScenariosPanel`)
25. `lib\grpc\grpc_channel.dart:1:8` — `unused_import` (`foundation.dart`)

---

## 4. Keepers & Targets Physical Verification
Every single file listed in the unified plan has been physically verified against the `lib/` directory structure.

### 4.1 Verified Keepers (Do NOT delete)
| Path Pattern / File | Physical Verification Status | Description / Content |
|---|---|---|
| `digital_brain_ui/**` | **Verified Present** (18 files) | Common UI kit (glass surfaces, glow elements, adaptive density) |
| `rfw_host/**` | **Verified Present** (7 files) | RFW rendering system (`synapse_stream_scope.dart`, `rfw_runtime_host.dart`) |
| `theme/**` | **Verified Present** (1 file) | `digitalbrain_theme.dart` hosting core colors and font styling |
| `grpc/**` | **Verified Present** (16 files) | Auto-generated Protobuf stubs and helper channel scripts |
| `telemetry/**` | **Verified Present** (11 files) | Telemetry, logging, metrics, and platform environments |
| `shell/**` | **Verified Present** (2 files) | Shell-level UI client scopes and scrubbing events |
| `features/brain/widgets/floating_prompt_dock.dart` | **Verified Present** | Floating bar allowing prompt submission and audio/speech trigger |
| `features/brain/voice_input.dart` | **Verified Present** | Voice/speech integration module |
| `features/live/**` | **Verified Present** (18 files) | Interactive live canvas, neuron cards, timeline strip, brain painter |
| `features/neuron_constructor/visual_constructor_models.dart` | **Verified Present** | State data models reused in Slice S4 |
| `features/neuron_constructor/visual_constructor_state.dart` | **Verified Present** | Constructor state-management models reused in Slice S4 |

### 4.2 Verified Targets (To delete in this slice)
| Target Path / File | Physical Verification Status | Line Count | Description / Role |
|---|---|---|---|
| `lib/features/brain/brain_scene_screen.dart` | **Verified Present** | 6,474 lines | Heavyweight legacy main screen; replaced by `LivingCanvasScreen` |
| `lib/features/home/constructor_editor_home_page.dart` | **Verified Present** | 3,840 lines | Legacy editor home screen; replaced by `LivingCanvasScreen` |
| `lib/features/constellation/constellation_screen.dart` | **Verified Present** | 3,923 lines | Legacy comparative screen; replaced by `LivingCanvasScreen` |
| `lib/features/constellation/brain_camera.dart` | **Verified Present** | 134 lines | Constellation camera controller; folded |
| `lib/features/constellation/brain_mesh.dart` | **Verified Present** | 100 lines | Constellation rendering mesh; folded |
| `lib/features/constellation/brain_node_widget.dart` | **Verified Present** | 114 lines | Constellation widget; folded |
| `lib/features/constellation/comparative_harness_widget.dart` | **Verified Present** | 313 lines | Shared testing comparative widget; folded |
| `lib/features/neuron_constructor/neuron_constructor_view.dart` | **Verified Present** | 2,885 lines | Legacy INO-coupled constructor interface; deleted |
| `lib/features/neuron_constructor/liquid_glass_3d_brain.dart` | **Verified Present** | 704 lines | Legacy 3D interactive brain scene; deleted |

### 4.3 Sweeper Candidates (To verify iteratively after deletions)
These files reside in target sweep directories (e.g., `ino_editor/` and `widgets/`) and will be pruned by the implementer as their inbound references drop to zero:
* **`ino_editor/` files**: `editor_card_source.dart`, `ino_editor_bus.dart`, `ino_source_subscription.dart`, `ino_syntax_highlight_controller.dart`, `llm_settings_bus.dart`, `prompt_input_bus.dart`, `state_editor_bus.dart`, `typewriter_controller.dart`
* **Stale widgets**: `widgets/brain_canvas.dart`, `widgets/brain_canvas_2d_graph.dart`, `widgets/brain_video_player.dart`, `widgets/brand_wordmark.dart`, `widgets/canvas_3d.dart`, `widgets/digitalbrain_widgets.dart`, `widgets/neuron_vector_logo.dart`, `widgets/option_chip_stack_card.dart`
