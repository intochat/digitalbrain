# Handoff Report: Synapse Display & Build Action Integration in Neuron Editor

## 1. Observation
We have inspected the Flutter UI feature directory and analyzed the neuron editor, synapse extraction, and build staging flows. The following exact code patterns and paths were observed:

- **Selected Neuron Editor Rendering**:
  - The neuron editor layout container is implemented as `_EditorBody` inside `UI/flutter/lib/features/brain/brain_scene_screen.dart` (lines 2569–2809), styled as a heavy overlay card inside an `AdaptiveSurface` container animated from `_editorOrigin` (lines 2333–2361).
  - The declarative UI layout is written in RFW format inside `UI/flutter/lib/features/ino_editor/editor_card_source.dart` (lines 4–87) as the stable constant `kInoEditorCardSource`.
  - In `_EditorBody.build` (line 2750), the host loads this layout via `widget.host.ensureLoaded(_EditorBody._kDocKey, kInoEditorCardSource)` and draws it with:
    ```dart
    widget.host.render(
      _EditorBody._kDocKey,
      data: { ... },
      onEvent: (name, args) => _localEvents.dispatch(name, args),
      rootWidget: 'InoEditorCard',
    )
    ```

- **Synapse Retrieval & Display**:
  - Synapses associated with the edited neuron are retrieved in `_EditorBodyState` via the `_synapseList` getter (lines 2609–2641). It uses static defaults (`DB.Google.Auth`, `DB.Signal.Alarm`, `DB.LLM.Prompt`) combined with a dynamic regex scanner checking the code text buffer:
    ```dart
    final regex = RegExp(r'\bon\s+synapse\s*\(\s*(DB\.[a-zA-Z_]\w*(?:\.[a-zA-Z_]\w*)*)\s*\)');
    ```
  - Synapses are displayed inside the RFW `InoEditorCard` tab `"synapses"`. It maps over `data.synapses` and renders `SynapseRow` widgets.
  - In `UI/flutter/lib/features/rfw_gallery/brainos_rfw_library.dart` (lines 2751–2814), `_synapseRow` maps events `'createHandler'` (which appends code to the script buffer) and `'fireSynapse'` (which increments the execution count and triggers 3D scene visual effects).

- **Associated Signals Catalog Retrieval**:
  - The editor loads a live contract schema dictionary using `QueryCatalogContractsRequest` over gRPC in `_CodeEditorBodyState._loadCatalog` (lines 1824–1861).
  - It maps definitions into `CatalogContractSchema` representing Synapses (`0`), Signals (`1`), and Neurons (`2`).
  - These catalog definitions drive autocomplete suggestions in `_checkAutocomplete` (lines 1932–2043) and live glassmorphic hover overlays detailing categories and fields in `_showHoverCard` (lines 1459–1588).

- **Build Action & Failure Banners**:
  - In the code tab, a **Premium Floating Staging Panel** is placed at the top-right overlay stack of `_CodeEditorBody` (lines 2169–2196), drawing compile status and button widgets (`_buildCompileButton()` at line 1713).
  - The build trigger invokes `_runCompileAndStage()` (lines 1596–1675) which evaluates local semantic validation gates (`BOSN001`–`BOSN005`).
  - If compilation fails, `_buildCompileDiagnosticsConsole()` (lines 1746–1813) renders a glassmorphic failure banner directly underneath the code block styling errors in JetBrains Mono font (`BrainOSColors.inkMid`) inside a soft red boundary (`BrainOSColors.rose.withValues(alpha: 0.3)`).

---

## 2. Logic Chain
1. **Selection & Display**:
   - *Observation*: User requests to edit a neuron script call `_openScriptEditor(neuronId, origin)` in `brain_scene_screen.dart:732`, setting `_editorOpen = true`.
   - *Reasoning*: This mounts the `_EditorBody` widget inside `AdaptiveSurface` (using `SurfaceWeight.heavy`), passing it the accumulated script text via `_inoSubscription`. The transition animates directly from the 3D neuron's coordinates.
2. **Synapse Identification & Closed-Loop Triggers**:
   - *Observation*: `_synapseList` in `brain_scene_screen.dart:2609` uses `RegExp` to extract `on synapse(...)` blocks from the code, and passes them to RFW which renders `SynapseRow` widgets.
   - *Reasoning*: This ensures any new synapse event handlers declared in the `.ino` script are parsed dynamically. Tapping `"Create Handler"` appends a handler scaffold directly to the editor's text buffer. Tapping `"Fire Synapse"` fires local telemetry increments and gRPC-bound UI canvas wave animations.
3. **FQN and Signal Discovery**:
   - *Observation*: `_loadCatalog` in `brainos_rfw_library.dart:1824` fetches live schemas from the kernel gateway and registers them in `_catalog` (with classifications for synapses, signals, and neurons).
   - *Reasoning*: Having access to the catalog schemas allows the `InoLangTextEditingController` to identify dotted words in real time, highlight them, show hover definition popups with fields, and offer context-aware autocomplete suggestions.
4. **Compilation Pipeline**:
   - *Observation*: The compile button in `_buildCompileButton()` calls `_runCompileAndStage()` in `brainos_rfw_library.dart:1596` which populates `_compileStatus` and `_compileErrors`.
   - *Reasoning*: Changing `_compileStatus` to `'error'` automatically displays the glassmorphic diagnostic console (`_buildCompileDiagnosticsConsole()`) below the editor block, while a `'success'` status hides the console and raises a success SnackBar.

---

## 3. Caveats
- **Local Simulation**: The compiler validation is currently simulated locally on the client (using regex semantic gates). A complete production-ready build action requires integrating backend gRPC compilation (`CompileNeuronRequest`).
- **Signal Rendering**: The "Synapses" tab currently lists only incoming synapses. Associated outbound signals extracted from the code are not yet rendered in a dedicated list, but they are fully supported in the backend catalog overlays.

---

## 4. Conclusion
The neuron editor renders a selected neuron's card by loading `kInoEditorCardSource` as an RFW document, embedding interactive native sub-widgets (`CodeEditor`, `PromptInput`, `StateEditor`) within the layout. Synapses are dynamically extracted from the active script using regular expressions and rendered inside the Synapses tab as interactive `SynapseRow` widgets that can trigger mock signals or scaffold code block handlers. Signals are cataloged using introspection gRPC calls and displayed as live hover cards and autocomplete entries. The build action is triggered from a premium floating staging panel, executing compile checks and displaying errors inside a glassmorphic compiler diagnostics console if validation gates fail.

---

## 5. Verification Method
To verify the editor layout and compilation flows:
1. **Inspect Layout and Widgets**:
   - Open `UI/flutter/lib/features/ino_editor/editor_card_source.dart` and confirm the `InoEditorCard` RFW split fraction, tabs, and child widgets.
   - Open `UI/flutter/lib/features/rfw_gallery/brainos_rfw_library.dart` and inspect the custom RFW mappings `SynapseRow`, `CodeEditor`, `PromptInput`, `LlmSettingsPanel`, and `TelemetryPanel`.
2. **Review Compilation & Diagnostics Code**:
   - Check `_CodeEditorBodyState._runCompileAndStage` and verify the compilation status transition and SnackBar calls.
   - Review `_CodeEditorBodyState._buildCompileDiagnosticsConsole` to ensure the layout matches the glassmorphic styling constraints.
3. **Verify Synapse dynamic extraction**:
   - Review `_EditorBodyState._synapseList` in `brain_scene_screen.dart` to verify that both preloaded synapses and dynamically declared ones are captured.
