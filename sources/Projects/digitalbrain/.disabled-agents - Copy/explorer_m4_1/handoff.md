# Handoff Report: InoLang Editor UI Structured Analysis

This report is submitted by Milestone 4 Explorer 1 in a read-only investigation capacity. It details critical observations, logic chains, caveats, conclusions, and verification methods for integrating syntax highlighting, FQN parsing, and inline hover cards into the Flutter Neuron Editor UI.

---

## 1. Observation

Direct code observations from the `e:/digitalbrain/UI/flutter` workspace:

1. **Neuron Editor Raw Code Display**:
   - Located as a heavy overlay card (`_EditorBody` widget) inside `UI/flutter/lib/features/brain/brain_scene_screen.dart` (lines 2569–2809). It renders in an `AdaptiveSurface` container using `SurfaceWeight.heavy` and morphs from the neuron's 3D coordinates using `morphFrom: _editorOrigin`.
   - Uses RFW source code `kInoEditorCardSource` inside `UI/flutter/lib/features/ino_editor/editor_card_source.dart`, mapping `'CodeEditor': _codeEditor` in `brainos_rfw_library.dart` (line 67).
   - In `brainos_rfw_library.dart`, `_codeEditor` instantiates `_CodeEditorBody` (line 1254) which wraps a standard `TextField` with `InoLangTextEditingController` (line 1266).
   - `InoLangTextEditingController.buildTextSpan` (line 1277) highlights keywords, comments, strings, and dotted FQNs (`match.group(5)`) using a static color `BrainOSColors.goldSoft` (line 1346).

2. **Inline FQN Parsing & Hover Cards**:
   - Dotted FQNs are parsed using regex: `r'(\b[a-zA-Z_]\w*(?:\.[a-zA-Z_]\w*)+\b)'` (line 1296 of `brainos_rfw_library.dart`).
   - Hover overlays are managed in `_CodeEditorBodyState` by `_showHoverCard` (line 1459) which fetches the schema details from `_catalog` and pops up an `OverlayEntry` built with image blur and Outlined borders.

3. **Creator Prompt Input**:
   - Rendered using `_PromptInputBody` in `brainos_rfw_library.dart` (line 2347). It uses a standard, un-highlighted `TextEditingController` (line 2363) which has no reference to the catalog or hover card triggers.

4. **Catalog Coordination**:
   - `_CodeEditorBodyState` fetches the contract catalog asynchronously inside `_loadCatalog()` (lines 1824–1861) targeting the gRPC envelope:
     ```dart
     ..typeName = 'BrainOS.Kernel.Contracts.Introspector.QueryCatalogContractsRequest'
     ```
   - RFW `TabViewer` child selector unmounts inactive children (line 2529). Disposing `.ino Code` tab destroys the state and forces catalog reloading on remount.

---

## 2. Logic Chain

1. **State Isolation**: Because `_loadCatalog` resides inside `_CodeEditorBodyState`, the catalog is destroyed whenever the user switches tabs, forcing redundant introspections (Section 1.4). Sibling widgets like `_PromptInputBody` (used for the plain English prompt) have no access to the catalog (Section 1.3).
2. **Centralization Solution**: A centralized catalog manager singleton (`BrainOSCatalogManager`) avoids these redundant requests by caching the loaded `CatalogContractSchema` list. Once loaded by any widget, it is immediately available to all sibling widgets synchronously.
3. **Plain English FQN Parsing**: To support inline FQNs (like `Google.Auth.New` or `DigitalBrain.SDK.*`) referenced in plain English, we must replace the unhighlighted `TextEditingController` of `_PromptInputBody` with `PromptTextEditingController` (Section 2.C in `analysis.md`). This custom controller parses dotted FQNs, resolves them against `BrainOSCatalogManager`, highlights them, and hooks up pointer hover triggers.
4. **Unified Hover Cards**: The `PromptTextEditingController` hover enter/exit listeners can target the same `OverlayEntry` show/hide methods inside `_PromptInputBodyState`, visually unifying hover cards across the prompt and editor panes.
5. **Real-time Staging & Compilation**: Rather than using simulated validation timers in `_runCompileAndStage()`, the editor should invoke live compilation by serializing `CompileNeuronRequest` into a `SynapseEnvelope` sent over `BrainOSGatewayClient`. Success updates the compiled C# Script view, while errors populate the diagnostic console (`_buildCompileDiagnosticsConsole`).

---

## 3. Caveats

- **Offline / Silo Disconnection**: If the silo is down, the client catalog query will fail. We recommend a local offline asset fallback (loading from a bundled JSON `assets/ino-catalog.json`) to keep the editor operational with generic autocomplete/highlight modes (Fail-safe TC-2.3-1).
- **Mobile/Web Constraints**: We did not analyze mobile-specific keyboard overlay adjustments or browser-level canvas layout changes.

---

## 4. Conclusion

The integration of syntax highlighting, inline hover cards, and FQN parsing requires extracting the catalog fetching pipeline into a shared `BrainOSCatalogManager` singleton, equipping the Creator Prompt with a custom `PromptTextEditingController` that resolves references against the singleton catalog, and wiring compilation actions directly to Orleans-based gRPC `CompileNeuronRequest` endpoints.

---

## 5. Verification Method

1. **Files to Inspect**:
   - `UI/flutter/lib/features/rfw_gallery/brainos_rfw_library.dart`: Ensure `BrainOSCatalogManager` and `PromptTextEditingController` classes are loaded.
   - `UI/flutter/lib/features/brain/brain_scene_screen.dart`: Check that `_EditorBodyState` uses the centralized catalog cache.
2. **Project Test Command**:
   Execute the full BDD / E2E integration test suite:
   ```powershell
   dotnet test UI\BrainOS.E2E.Tests\BrainOS.E2E.Tests.csproj
   ```
3. **Invalidation Condition**:
   If the gRPC channel becomes disconnected, catalog resolving should fallback gracefully to a bundled `.ino-catalog.json` asset without causing UI crashes.
