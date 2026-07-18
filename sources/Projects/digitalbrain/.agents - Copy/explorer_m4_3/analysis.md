# Structured Analysis Report: Synapse Display & Build Action Integration

## Executive Summary
This report analyzes the user experience, rendering loop, state synchronization, and compile pipelines of the Neuron Editor inside the `UI/flutter` desktop shell. The objective is to identify:
1. Where and how the neuron editor renders a selected neuron's card.
2. How the list of synapses handled by a given neuron and their associated signals are retrieved and displayed.
3. How the build action is triggered from the neuron editor, how it communicates compile/build options, and how compilation failure diagnostics are presented to the user.

Key findings show a highly structured, hybrid architecture: the main Neuron Editor is rendered using **Remote Flutter Widgets (RFW)** for declarative UI composition, backed by standard Flutter widgets for interactive code editing, auto-completion, hover cards, catalog retrieval, and client-side compilation verification.

---

## 1. Selected Neuron Card Rendering Analysis
The selected neuron editor is displayed as a heavy overlay card styled as `_EditorBody` inside `UI/flutter/lib/features/brain/brain_scene_screen.dart` (lines 2569–2809).

### A. Lifecycle & Presentation
- **Activation Context**: When a user selects a neuron script link (e.g., from the active neurons list or the visual brain graph), `_openScriptEditor(neuronId, origin)` or `_openEditor(origin)` is called. This sets `_editorOpen = true` and opens an `InoSourceSubscription` session (using the ID `'editor-<neuronId>'`).
- **Visual Presentation**: The editor renders inside an `AdaptiveSurface` container using `SurfaceWeight.heavy` (lines 2333–2361).
- **Interactive Transitions**: It morphs dynamically from `_editorOrigin` (the 3D canvas coordinates of the selected neuron) using the `morphFrom` property, accompanied by a `CollapseWave` screen animation, providing a seamless spatial transition.
- **State Preservation**: The editor subscribes to `_inoSubscription` via a `ListenableBuilder` (lines 2762–2807). When text chunks stream in from the gRPC backend, the editor captures the state, updates the text, and triggers RFW document updates.

### B. Remote Flutter Widgets (RFW) Architecture
- **Document Source**: The layout is declared in `kInoEditorCardSource` inside `UI/flutter/lib/features/ino_editor/editor_card_source.dart`. It is written with explicit `'\n'` concatenation to safeguard the constant against git `autocrlf` changes and ensure byte-stability.
- **Parsing and Rendering**: In `_EditorBody.build` (line 2750), the host parses the RFW layout with `widget.host.ensureLoaded(_EditorBody._kDocKey, kInoEditorCardSource)` and draws it with:
  ```dart
  widget.host.render(
    _EditorBody._kDocKey,
    data: {
      'source': source,
      'activeTab': _activeTab,
      'typing': _typing,
      'csharpSource': _csharpSource,
      'stateJson': jsonEncode(_neuronState),
      'synapses': _synapseList,
      'llmModel': _llmModel,
      'llmTemp': _llmTemp,
      'llmMaxAttempts': _llmMaxAttempts,
      'genAttempts': _genAttempts,
      'execRuns': _execRuns,
      'failedRuns': _failedRuns,
      'tab_code': _activeTab == 'code',
      ...
    },
    onEvent: (name, args) => _localEvents.dispatch(name, args),
    rootWidget: 'InoEditorCard',
  )
  ```
- **RFW Layout Structure**:
  - The card utilizes a `Split` widget with `leftFraction: 0.35`.
  - **Left Pane (`VStack`)**: Contains the Creator Prompt input (`PromptInput`), AI model hyperparameter tuning controls (`LlmSettingsPanel`), and overall attempt metrics (`TelemetryPanel`).
  - **Right Pane (`VStack`)**: Renders a custom tab bar (`TabButton`s) linking to a `TabViewer` hosting seven distinct tabs:
    1. **.ino Code Tab**: Draws `CodeEditor(text: data.source, typing: data.typing, language: "ino")`.
    2. **C# Script Tab**: Renders `CodeEditor(text: data.csharpSource, typing: false, language: "csharp")`.
    3. **State Editor Tab**: Renders `StateEditor(stateJson: data.stateJson, onUpdate: event "updateState")`.
    4. **Synapses Tab**: Generates the list of inputs and fires dynamic triggers (`SynapseRow`).
    5. **Telemetry Tab**: Visualizes performance metrics (throughput and P95 latencies).
    6. **NeuronML Tab**: Renders neural pathway placeholders.
    7. **Simulations Tab**: Renders test scenarios.

---

## 2. Synapse and Signal Display & Integration Analysis

### A. How Synapses Handled by a Neuron are Retrieved
The list of synapses is computed dynamically by the getter `_synapseList` in `brain_scene_screen.dart` (lines 2609–2641). It utilizes a multi-step strategy:
1. **Preloaded Base Synapses**: A default list is defined to ensure standard functionality exists:
   - `DB.Google.Auth` (Google authentication)
   - `DB.Signal.Alarm` (Interval timers and notifications)
   - `DB.LLM.Prompt` (Direct user text prompts)
2. **Dynamic Semantic Extraction**: A regular expression scans the active editor script `widget.subscription.accumulated` to extract synapses declared as event handlers in the script:
   - `RegExp(r'\bon\s+synapse\s*\(\s*(DB\.[a-zA-Z_]\w*(?:\.[a-zA-Z_]\w*)*)\s*\)')`
   - Any matches (e.g. `on synapse(DB.Ai.Request)`) are registered and appended to the handled list.

### B. How Synapses are Rendered
When the user selects the `"Synapses"` tab, the RFW document loops over the computed synapses array and outputs `SynapseRow` widgets. On the Flutter host side, `_synapseRow` (defined in `rfw_gallery/brainos_rfw_library.dart` line 2751) translates this into a standard material row:
- Displays the synapse FQN and its descriptive behavior.
- Provides two core interactions:
  - **Create Handler** (`onCreate`): Appends an automatic handler block directly to the `.ino` buffer:
    ```
    on synapse(<type>) it:
        // Handle incoming <type> signal
        save it.payload into state.lastMessage;
    ```
    This switches the editor tab to code editing and triggers a typewriter entry animation.
  - **Fire Synapse** (`onFire`): Simulates closed-loop execution. It sends an event wave across the 3D scene from the parent neuron's location, increments the local state execution counts, updates `lastTrigger` timestamps, and displays a success SnackBar.

### C. Live Catalog & Signal Display
Under the hood of the Code Editor, `_CodeEditorBodyState._loadCatalog` (in `brainos_rfw_library.dart` lines 1824–1861) executes a gRPC call to retrieve live contracts:
- **gRPC Introspection Query**: It serializes a transaction metadata payload and constructs a `SynapseEnvelope` targeting `'BrainOS.Kernel.Contracts.Introspector.QueryCatalogContractsRequest'`.
- **Parsing Schema Definitions**: It maps the incoming JSON array into `CatalogContractSchema` instances consisting of:
  - `fqn`: Dotted fully qualified name (e.g., `DB.Signal.Alarm`).
  - `kind`: Categorization (`0` = Synapse, `1` = Signal, `2` = Neuron).
  - `fields`: A collection of properties representing variables carried by the contract payload.
- **Hover Integration**: The `InoLangTextEditingController` parses dotted words in real time. Hovering over a valid catalog FQN triggers `_showHoverCard`, creating a glassmorphic overlay overlay displaying:
  - The contract type category (e.g. `SYNAPSE` or `SIGNAL`).
  - The exact FQN.
  - The schema fields (e.g. `• success`, `• prompt`, `• email`).
- **Auto-completion Support**: When typing inside the code field, `_checkAutocomplete` matches user input (like `signal(` or property accesses `$mySyn.`) against the schemas in `_catalog` and presents relevant autocomplete suggestions.

---

## 3. Build Action Integration & Compile Failure Banner Flow Analysis

### A. How the Build Action is Triggered
- The compilation trigger is hosted directly inside the `.ino` Code Editor (`_CodeEditorBody` in `brainos_rfw_library.dart`).
- A **Premium Floating Staging Panel** is placed at the top-right overlay of the code editor stack (lines 2169–2196). It draws an interactive compile button (`_buildCompileButton()` at line 1713).
- Tapping this button invokes `_runCompileAndStage()`.

### B. Current Compilation & Validation Sequence
Currently, the staging action is simulated locally on the client using a short delayed task. It runs the following semantic gates:
- **`BOSN001`**: Ensures a dotted neuron FQN is declared in the script (`neuron BrainOS.Examples.MyNeuron`).
- **`BOSN002`**: Ensures the code contains a `scenario` block (L6 Gate verification for test-driven stubs).
- **`BOSN003`**: Checks that all `using` aliases (e.g., `using auth = synapse(...)`) point to existing contracts inside `_catalog`.
- **`BOSN004` & `BOSN005`**: Validates balanced parentheses `()` and balanced brackets `[]`.

### C. Backend gRPC Integration Model
To replace the simulated compilation with a production-ready Roslyn scripting compile and stage action, the frontend will package the build request into the unified gRPC communication gateway:
1. **Request Packaging**: Packaging options and code into a `SynapseEnvelope`:
   - `typeName`: `'BrainOS.Kernel.Contracts.Compiler.CompileNeuronRequest'`
   - `correlationId`: matching the current editor subscription ID.
   - `payload`: JSON structure passing the target neuron FQN, full `.ino` or C# source code, and configuration options:
     ```json
     {
       "neuronId": "BrainOS.Kernel.Creator.CreatorNeuron",
       "sourceCode": "on synapse(DB.LLM.Prompt) it: ...",
       "options": {
         "optimizationLevel": "Release",
         "enableRoslynScripting": true,
         "testDrivenLoopMode": true,
         "mockLlmStubs": true,
         "stageLive": true
       }
     }
     ```
2. **Submission**: Dispatched via `BrainOSGatewayClient.send(envelope)`.
3. **Response Handling**: The kernel compiles the script in-memory, executes static test scenario checks, stages the neuron actor, and replies with a `CompileNeuronResponse` envelope. The client decodes the result:
   - **On Success**: Sets `_compileStatus = 'success'`, updates the `C# Script` tab content with compiled output, and pops a success Snackbar.
   - **On Failure**: Sets `_compileStatus = 'error'` and parses the error list.

### D. Compile Failure Banners (Diagnostics Console)
- When compilation fails, `_compileStatus` transitions to `'error'` and the diagnostic errors are passed into `_compileErrors`.
- This triggers `_buildCompileDiagnosticsConsole()` (lines 1746–1813) which places a console panel directly underneath the code block:
  - **Visual Design**: The panel is a glassmorphic container with a BackdropFilter blur of `8` and a deep translucent black color (`alpha: 0.8`).
  - **Error Borders**: A soft red boundary (`BrainOSColors.rose.withValues(alpha: 0.3)`) marks the container.
  - **Layout & Diagnostics**: Features a bold header `"COMPILER DIAGNOSTICS (<count> errors)"` in Outfit font, accompanied by a warning icon. It loops through `_compileErrors` and prints each diagnostic item in JetBrains Mono font (`BrainOSColors.inkMid`) with a standard line-height of `1.4` for clean readability.

---

## 4. Synthesis & Logic Chain

The architecture of the neuron editor ensures high modularity, splitting layout composition from execution logic. Below is the step-by-step evidence chain supporting our operational synthesis:

```
[Observation: InoEditorCard RFW Source]
   |--> Defined in editor_card_source.dart. Uses declarative RFW vocabulary.
   |--> Renders a TabViewer containing a CodeEditor, StateEditor, and SynapseRows.
   |
   +--> [Observation: brainos_rfw_library.dart]
         |--> Registers the widget mappings ('CodeEditor', 'SynapseRow', etc.)
         |--> _codeEditor parses and renders InoLangTextEditingController.
         |--> Implements hover enter/exit listeners for dotted FQNs.
         |--> _loadCatalog queries QueryCatalogContractsRequest via gRPC.
         |--> _runCompileAndStage executes compile validation check gates.
         |--> _buildCompileDiagnosticsConsole renders compiler errors.
         |
         +--> [Observation: brain_scene_screen.dart]
               |--> Hosts _EditorBody inside an AdaptiveSurface modal stack.
               |--> Binds events (selectTab, createHandler, fireSynapse) to local state mutations.
               |--> Dynamic extraction _synapseList extracts synapses from current code text.
```

### Strategic Reconciliations
- **Syntax Highlighting & Hover Integration**: Standard RFW text spans do not easily capture complex cursor hover coordinates or rich multi-color syntax highlighting. To solve this, `brainos_rfw_library.dart` escapes RFW constraints by rendering a native `TextField` with a custom `InoLangTextEditingController` inside the `_codeEditor` widget. This allows high-performance regular expression parsing, click actions, overlay entries, and native text highlights that are impossible in pure RFW.
- **Dynamic Synapse Discovery**: Because RFW is a fixed layout, the list of handled synapses cannot be statically declared. The host-level Flutter controller manages this by dynamically scanning the code buffer on every change. RFW receives this as a dynamic array and renders the rows declaratively.

---

## 5. Implementation Recommendations

To transition the current editor card from a local simulator to a fully connected production-ready staging pipeline matching Milestone 4 targets, the following steps are recommended:

### A. Extend the Contract Schema for Signals
Introduce a classification model to separate Synapses and Signals explicitly inside the Catalog.
1. Update `CatalogContractSchema` in `brainos_rfw_library.dart` to support distinct parsing for kind `1` (Signal).
2. Within the hover logic (`_showHoverCard`), style `SIGNAL` hover cards with a dedicated gold theme (`BrainOSColors.goldSoft`) and `SYNAPSE` hover cards with a teal theme (`BrainOSColors.tealSoft`) to visually reinforce contract types.

### B. Integrate the Production gRPC Build Action
Implement live backend compilation using the unified C# SDK and Orleans Kernel.
1. **Define Compile Contract**: Add request and response contracts inside the proto:
   ```protobuf
   message CompileNeuronRequest {
     string neuron_id = 1;
     string source_code = 2;
     string optimization_level = 3;
     bool enable_roslyn = 4;
   }
   message CompileNeuronResponse {
     bool success = 1;
     repeated string errors = 2;
     string generated_csharp = 3;
   }
   ```
2. **Execute Unified Compile call**: Inside `_CodeEditorBodyState._runCompileAndStage()`, replace the simulated validation delay with a gRPC unary call:
   ```dart
   final request = CompileNeuronRequest()
     ..neuronId = _zoomedNeuron
     ..sourceCode = _textController.text
     ..optimizationLevel = 'Release'
     ..enableRoslyn = true;

   try {
     final response = await client.compileNeuron(request);
     setState(() {
       if (response.success) {
         _compileStatus = 'success';
         _csharpSource = response.generatedCsharp;
         // Trigger a reload of the C# script tab...
       } else {
         _compileStatus = 'error';
         _compileErrors = response.errors;
       }
     });
   } catch (e) {
     setState(() {
       _compileStatus = 'error';
       _compileErrors = ['Failed to communicate with kernel compiler: $e'];
     });
   }
   ```

### C. Connect Associated Signals in the Synapse Tab
Currently, the Synapse Tab only lists inbound synapses. We should list associated signals.
1. Extend `InoEditorCard` RFW source under tab 3 (`Synapses`) to render both Incoming Synapses and Outbound Emitted Signals:
   ```rfw
   SectionLabel(text: "Emitted Outbound Signals"),
   ...for sig in data.signals:
     SignalRow(
       type: sig.type,
       desc: sig.desc,
       onEmit: event "emitSignal" { type: sig.type },
     ),
   ```
2. Parse emitted signals from the code text:
   - Identify `await fire synapse(...) with ...` or `emit ...` keywords.
   - Filter FQNs of kind `1` (Signal) from the catalog.
   - Bind the `emitSignal` event to a test-fire utility inside `_EditorBody` that triggers a visual pulse from the editor.
