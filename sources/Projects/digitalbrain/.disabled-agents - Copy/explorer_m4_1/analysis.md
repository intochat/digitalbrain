# Structured Analysis Report: InoLang Editor Integration (Syntax Highlighting, FQN Parsing, Hover Cards & Catalog Coordination)

## Executive Summary
This report presents a comprehensive technical analysis of the InoLang Editor in the `UI/flutter/` shell, synthesizing findings from our investigative cycle. We analyze the current Remote Flutter Widgets (RFW) editor architecture, define a robust mechanism for parsing and highlighting inline Fully Qualified Names (FQNs) within plain English text, and propose a centralized coordination paradigm for the contract catalog (`_catalog`).

We detail concrete implementation recommendations, including before-and-after code modifications and a formal verification plan to ensure a production-ready, highly interactive editor experience.

---

## 1. Current Neuron Editor Raw Code Display Architecture

The Selected Neuron Editor is represented in the client as a high-fidelity panel overlaid as an `AdaptiveSurface` in the Flutter shell.

### A. Surface Lifecycle & Animation Transitions
- **Activation**: When a user selects a neuron script or triggers an edit link (e.g., from the active neurons sidebar or the 3D visual brain graph), `_openScriptEditor(neuronId, origin)` is invoked in `brain_scene_screen.dart`. This opens an `InoSourceSubscription` session and sets `_editorOpen = true`.
- **Morph Transitions**: The surface uses `morphFrom` based on the 3D screen position (`_editorOrigin`), coupled with a `CollapseWave` ripple animation, to provide a smooth, context-preserving zoom from the node in the brain graph into the editor card.
- **State Coupling**: State is managed asynchronously through `InoEditorBus` and `InoSourceSubscription`, updating the `.ino` text area dynamically as raw code streams from the Orleans Kernel.

### B. Remote Flutter Widgets (RFW) Layout
The editor UI is declared in `kInoEditorCardSource` (`editor_card_source.dart`) using RFW markup:
- **TabViewer Pane**: Features seven tabs: `.ino Code`, `C# Script`, `State Editor`, `Synapses`, `Telemetry`, `NeuronML`, and `Simulations`.
- **Tab Destruction**: The `_tabViewer` implementation in `brainos_rfw_library.dart` (lines 2517–2532) returns only the active tab index:
  ```dart
  final index = tabs.indexOf(activeTab);
  if (index >= 0 && index < children.length) {
    return children[index];
  }
  ```
  This means switching tabs destroys and disposes the inactive tab's widget tree. Returning to the `.ino Code` tab recreates `_CodeEditorBodyState` and forces a reload of the contract catalog.

### C. Rich Text Editing & Highlighting Seam
Because declarative RFW widgets cannot handle real-time rich-text formatting, custom cursor movement, or overlay-based hover states:
- Mapped under `'CodeEditor'`, the library loads a stateful Flutter `_CodeEditorBody` widget (line 1251) containing a native `TextField` paired with `InoLangTextEditingController`.
- `InoLangTextEditingController` overrides `buildTextSpan` (line 1277) to dynamically lex the document using regex matching for keywords, strings, comments, dotted FQNs, and C# signatures, applying corresponding theme styles.

---

## 2. Inline FQN Highlighting in Plain English Prompt Text

### A. The Requirement
Users describe new neuron behaviors in plain English inside the **Creator Prompt** input panel (`PromptInput`, rendered via `_PromptInputBody`). We need to capture and highlight inline FQNs (e.g. `Google.Auth.New`, `DigitalBrain.SDK.Storage`, or wildcards like `DigitalBrain.SDK.*`) referencing the catalog in real-time, allowing users to hover over them to view signature hover cards.

### B. Implementation Seam
Currently, `_PromptInputBodyState` in `brainos_rfw_library.dart` (lines 2362–2445) uses a plain `TextEditingController` which lacks highlight logic:
```dart
class _PromptInputBodyState extends State<_PromptInputBody> {
  late final TextEditingController _controller = TextEditingController(
    text: PromptInputBus.instance.text,
  )..addListener(_pushToBus);
  ...
```

To support inline FQNs in plain English, we must:
1. Create a custom `PromptTextEditingController` extending `TextEditingController`.
2. Configure it with a regex that captures dotted FQNs and wildcard suffixes in plain text:
   `\b[a-zA-Z_]\w*(?:\.[a-zA-Z_]\w*)+(?:\.\*)?\b`
3. Resolve FQNs against the centralized contract catalog.
4. Support mouse interactions (`onEnter`, `onExit`) to trigger overlay hover cards, reusing the editor's hover overlay logic.

### C. Proposed Code Addition: `PromptTextEditingController`

```dart
class PromptTextEditingController extends TextEditingController {
  PromptTextEditingController({
    super.text,
    this.onHoverEnter,
    this.onHoverExit,
    required this.getCatalog,
  });

  final void Function(String fqn, PointerEnterEvent event)? onHoverEnter;
  final void Function(PointerExitEvent event)? onHoverExit;
  final List<CatalogContractSchema> Function() getCatalog;

  @override
  TextSpan buildTextSpan({
    required BuildContext context,
    TextStyle? style,
    required bool withComposing,
  }) {
    final defaultStyle = style ?? GoogleFonts.manrope(
      fontSize: 14,
      color: BrainOSColors.ink,
    );

    final List<InlineSpan> spans = <InlineSpan>[];
    int lastMatchEnd = 0;

    // Matches dotted FQNs (with optional wildcard .* at the end)
    final RegExp regex = RegExp(r'\b[a-zA-Z_]\w*(?:\.[a-zA-Z_]\w*)*(?:\.\*)?\b');

    for (final RegExpMatch match in regex.allMatches(text)) {
      final fqn = match.group(0)!;
      
      // Determine if FQN is in catalog (or matches wildcard)
      final catalog = getCatalog();
      final isWildcard = fqn.endsWith('.*');
      final baseFqn = isWildcard ? fqn.substring(0, fqn.length - 2) : fqn;
      
      final bool exists = isWildcard
          ? catalog.any((s) => s.fqn.toLowerCase().startsWith(baseFqn.toLowerCase()))
          : catalog.any((s) => s.fqn.toLowerCase() == fqn.toLowerCase());

      if (match.start > lastMatchEnd) {
        spans.add(TextSpan(
          text: text.substring(lastMatchEnd, match.start),
          style: defaultStyle,
        ));
      }

      if (exists) {
        // Highlight active referenced FQN
        spans.add(TextSpan(
          text: fqn,
          style: defaultStyle.copyWith(
            color: isWildcard ? BrainOSColors.violetSoft : BrainOSColors.goldSoft,
            fontWeight: FontWeight.bold,
          ),
          mouseCursor: SystemMouseCursors.click,
          onEnter: (event) => onHoverEnter?.call(fqn, event),
          onExit: (event) => onHoverExit?.call(event),
        ));
      } else {
        // Plain text if FQN doesn't exist in catalog
        spans.add(TextSpan(
          text: fqn,
          style: defaultStyle,
        ));
      }

      lastMatchEnd = match.end;
    }

    if (lastMatchEnd < text.length) {
      spans.add(TextSpan(
        text: text.substring(lastMatchEnd),
        style: defaultStyle,
      ));
    }

    return TextSpan(children: spans, style: defaultStyle);
  }
}
```

---

## 3. Centralized Catalog Coordination Paradigm

### A. The Coordination Defect
Currently, `_CodeEditorBodyState` fetches the contract catalog directly through a local `_loadCatalog()` method. This design is highly inefficient:
1. **Redundant Network Calls**: Because RFW destroys the code editor widget tree when switching tabs, `_CodeEditorBodyState` is disposed. Switching back triggers `didChangeDependencies` and executes another gRPC introspector query.
2. **State Isolation**: The `PromptInput` (Creator Prompt) has no way of obtaining the catalog, preventing it from highlighting FQNs or showing hover cards.

### B. Proposed Solution: Centralized `BrainOSCatalogManager`
We propose introducing a centralized catalog cache and fetch coordination layer. This can be built as a shared singleton or registered in the `BrainOSClientScope`. A static/singleton manager is highly reliable and easily consumed by both the editor and the prompt panels.

```dart
class BrainOSCatalogManager extends ChangeNotifier {
  BrainOSCatalogManager._privateConstructor();
  static final BrainOSCatalogManager instance = BrainOSCatalogManager._privateConstructor();

  List<CatalogContractSchema> _catalog = [];
  bool _isLoading = false;
  bool _loaded = false;
  Future<List<CatalogContractSchema>>? _activeFetch;

  List<CatalogContractSchema> get catalog => _catalog;
  bool get loaded => _loaded;

  Future<List<CatalogContractSchema>> loadCatalog(BrainOSGatewayClient client) {
    if (_loaded) return Future.value(_catalog);
    if (_isLoading && _activeFetch != null) return _activeFetch!;

    _isLoading = true;
    _activeFetch = _executeFetch(client);
    return _activeFetch!;
  }

  Future<List<CatalogContractSchema>> _executeFetch(BrainOSGatewayClient client) async {
    final requestPayload = jsonEncode({
      'SynapseId': '00000000-0000-0000-0000-000000000000',
      'CorrelationId': '00000000-0000-0000-0000-000000000000',
      'CallerNeuronId': '00000000-0000-0000-0000-000000000000',
      'CallerNeuronType': 'External',
      'ReceiverNeuronId': '00000000-0000-0000-0000-000000000000',
      'ReceiverNeuronType': 'IntrospectorNeuron',
      'Timestamp': DateTime.now().toUtc().toIso8601String(),
    });

    final envelope = SynapseEnvelope()
      ..correlationId = ''
      ..typeName = 'BrainOS.Kernel.Contracts.Introspector.QueryCatalogContractsRequest'
      ..payload = Uint8List.fromList(utf8.encode(requestPayload));

    try {
      final response = await client.send(envelope);
      final responsePayload = utf8.decode(response.payload);
      final responseData = jsonDecode(responsePayload) as Map<String, dynamic>;
      final schemasJson = (responseData['Schemas'] ?? responseData['schemas']) as List?;
      if (schemasJson != null) {
        _catalog = schemasJson
            .map((s) => CatalogContractSchema.fromJson(s as Map<String, dynamic>))
            .toList();
        _loaded = true;
      }
    } catch (e) {
      debugPrint('Failed to load contract catalog: $e');
      // Offline fallback: load from local bundle if available
    } finally {
      _isLoading = false;
      _activeFetch = null;
      notifyListeners();
    }
    return _catalog;
  }
}
```

### C. Integrating the Centralized Catalog in Widgets

#### 1. In `_CodeEditorBodyState`:
```dart
  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    final client = BrainOSClientScope.of(context);
    if (client != null) {
      BrainOSCatalogManager.instance.loadCatalog(client).then((loadedCatalog) {
        if (mounted) {
          setState(() {
            _catalog = loadedCatalog;
            _catalogLoaded = true;
          });
        }
      });
    }
  }
```

#### 2. In `_PromptInputBodyState`:
We integrate the hover card overlay, register the custom controller, and query the catalog manager synchronously:
```dart
class _PromptInputBodyState extends State<_PromptInputBody> {
  late final PromptTextEditingController _controller;
  OverlayEntry? _hoverCardEntry;
  String? _hoveredFqn;

  @override
  void initState() {
    super.initState();
    _controller = PromptTextEditingController(
      text: PromptInputBus.instance.text,
      getCatalog: () => BrainOSCatalogManager.instance.catalog,
      onHoverEnter: _handleHoverEnter,
      onHoverExit: _handleHoverExit,
    )..addListener(_pushToBus);
  }

  void _pushToBus() => PromptInputBus.instance.set(_controller.text);

  void _handleHoverEnter(String fqn, PointerEnterEvent event) {
    _showHoverCard(fqn, event.position);
  }

  void _handleHoverExit(PointerExitEvent event) {
    _hideHoverCard();
  }

  void _showHoverCard(String fqn, Offset position) {
    if (_hoveredFqn == fqn) return;
    _hideHoverCard();
    _hoveredFqn = fqn;

    final catalog = BrainOSCatalogManager.instance.catalog;
    
    // Support wildcard or single FQN lookup
    CatalogContractSchema schema;
    if (fqn.endsWith('.*')) {
      final base = fqn.substring(0, fqn.length - 2).toLowerCase();
      schema = catalog.firstWhere(
        (s) => s.fqn.toLowerCase().startsWith(base),
        orElse: () => CatalogContractSchema(fqn: '', kind: -1, fields: []),
      );
    } else {
      schema = catalog.firstWhere(
        (s) => s.fqn.toLowerCase() == fqn.toLowerCase(),
        orElse: () => CatalogContractSchema(fqn: '', kind: -1, fields: []),
      );
    }

    if (schema.kind == -1) return;

    final kindName = schema.kind == 0 ? 'SYNAPSE' : schema.kind == 1 ? 'SIGNAL' : 'NEURON';
    final accentColor = schema.kind == 0 ? BrainOSColors.tealSoft : schema.kind == 1 ? BrainOSColors.goldSoft : BrainOSColors.violetSoft;

    _hoverCardEntry = OverlayEntry(
      builder: (context) => Positioned(
        left: position.dx + 12,
        top: position.dy + 12,
        child: Material(
          color: Colors.transparent,
          child: ClipRRect(
            borderRadius: BorderRadius.circular(8),
            child: BackdropFilter(
              filter: ImageFilter.blur(sigmaX: 12, sigmaY: 12),
              child: Container(
                padding: const EdgeInsets.all(12),
                decoration: BoxDecoration(
                  color: Colors.black.withValues(alpha: 0.8),
                  borderRadius: BorderRadius.circular(8),
                  border: Border.all(color: Colors.white.withValues(alpha: 0.15)),
                ),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Row(
                      children: [
                        Container(
                          padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
                          decoration: BoxDecoration(
                            color: accentColor.withValues(alpha: 0.15),
                            borderRadius: BorderRadius.circular(4),
                            border: Border.all(color: accentColor.withValues(alpha: 0.4)),
                          ),
                          child: Text(kindName, style: GoogleFonts.outfit(fontSize: 9, fontWeight: FontWeight.bold, color: accentColor)),
                        ),
                        const SizedBox(width: 8),
                        Text(fqn, style: GoogleFonts.jetBrainsMono(fontSize: 11, fontWeight: FontWeight.bold, color: BrainOSColors.ink)),
                      ],
                    ),
                    if (schema.fields.isNotEmpty) ...[
                      const SizedBox(height: 8),
                      Text('FIELDS', style: GoogleFonts.outfit(fontSize: 8, fontWeight: FontWeight.bold, color: BrainOSColors.inkLow)),
                      const SizedBox(height: 4),
                      for (final field in schema.fields)
                        Text('• $field', style: GoogleFonts.jetBrainsMono(fontSize: 10, color: BrainOSColors.inkMid)),
                    ],
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
    Overlay.of(context).insert(_hoverCardEntry!);
  }

  void _hideHoverCard() {
    _hoveredFqn = null;
    _hoverCardEntry?.remove();
    _hoverCardEntry = null;
  }

  @override
  void dispose() {
    _hideHoverCard();
    _controller
      ..removeListener(_pushToBus)
      ..dispose();
    super.dispose();
  }
  ...
```

---

## 4. Synapse, Signal Display and Build Action Coordination

### A. Parsing & Displaying Synapses and Signals
The active `.ino` code uses explicit `using` statements to alias contracts:
```ino
using request   = synapse(BrainOS.Domains.Travel.Contracts.TripPlanRequested)
using ready     = signal(BrainOS.Domains.Travel.Contracts.TripPlanReady)
using groupChat = neuron(DigitalBrain.SDK.Ai.GroupChat.GroupChatNeuron)
```
1. **Code Parser Enhancement**: Expose `_synapseList` (in `brain_scene_screen.dart`) or code compilers to read both `using \w+ = synapse\(([^)]+)\)` and `using \w+ = signal\(([^)]+)\)` patterns.
2. **Divided Synapses & Signals List**: Under the editor's "Synapses" tab, split the display into two explicit sections:
   - **Incoming Synapses**: Shows synapses declared via `using` or event handlers, providing interactive `onCreate` (generate handler stubs) and `onFire` (fire/test synapse) actions.
   - **Emitted Signals**: Lists outbound signals associated with the neuron, with an interactive "Test Emit" action to visual-test signal emissions.

### B. Integrating Backend Roslyn Compilation (Build Action)
Instead of local simulation gates inside `_runCompileAndStage()`, trigger live dynamic compilation using the backend Orleans Roslyn compiler:
1. **gRPC Build Request**: Package compilation options into a `SynapseEnvelope`:
   - `typeName`: `'BrainOS.Kernel.Contracts.Compiler.CompileNeuronRequest'`
   - `payload`: JSON containing the script FQN, code buffer, and compiler properties (optimization level, scenario stubs, mock modes).
2. **Dispatch & Stage Pipeline**:
   ```dart
   final client = BrainOSClientScope.of(context);
   if (client == null) return;

   final requestPayload = jsonEncode({
     'NeuronId': 'BrainOS.Domains.Travel.TripPlanner.TripPlannerNeuron',
     'SourceCode': _textController.text,
     'Options': {
       'OptimizationLevel': 'Release',
       'EnableRoslynScripting': true,
       'TestDrivenLoopMode': true,
       'MockLlmStubs': true
     }
   });

   final envelope = SynapseEnvelope()
     ..correlationId = InoEditorBus.instance.activeSubscription?.correlationId ?? ''
     ..typeName = 'BrainOS.Kernel.Contracts.Compiler.CompileNeuronRequest'
     ..payload = Uint8List.fromList(utf8.encode(requestPayload));

   try {
     final response = await client.send(envelope);
     final responsePayload = utf8.decode(response.payload);
     final responseData = jsonDecode(responsePayload) as Map<String, dynamic>;
     
     if (responseData['Success'] == true) {
       setState(() {
         _compileStatus = 'success';
         _csharpSource = responseData['GeneratedCsharp'].toString();
       });
     } else {
       setState(() {
         _compileStatus = 'error';
         _compileErrors = List<String>.from(responseData['Errors'] ?? []);
       });
     }
   } catch (e) {
     setState(() {
       _compileStatus = 'error';
       _compileErrors = ['Failed to communicate with the compilation server: $e'];
     });
   }
   ```
3. **Diagnostics Console**: Display failure logs cleanly inside the custom failure diagnostics banner (`_buildCompileDiagnosticsConsole()`) using JetBrains Mono, styled with soft red borders and background blurs.

---

## 5. Logic Chain

1. **RFW Tab Lifecycles**: `TabViewer` acts as an index-selector and unmounts inactive children. Disposing the `.ino Code` tab destroys `_CodeEditorBodyState`, causing regular catalog reloads (Section 1.B).
2. **Catalog Isolation**: `_loadCatalog()` is isolated in the private state of the editor body, preventing the plain English Creator Prompt input from querying the catalog to perform parsing and highlighting (Section 2.B, 3.A).
3. **Centralization Benefits**: Building a centralized `BrainOSCatalogManager` singleton resolves the redundant loading issue by caching schema lists. It also provides a shared catalog API usable by both the prompt and editor text controllers synchronously (Section 3.B, 3.C).
4. **FQN Plain-English Parsing**: Pair the Creator Prompt text field with a `PromptTextEditingController` resolving against the centralized catalog manager to highlight inline FQNs in real time. It links into the editor's existing hover overlay pipelines to display glassmorphic card overlays (Section 2.C, 3.C).
5. **Production Build Pipeline**: Transitioning `_runCompileAndStage()` to gRPC-based `CompileNeuronRequest` messages integrates Orleans-side Roslyn dynamic compilation and feeds compilation diagnostics directly into the UI (Section 4.B).

---

## 6. Verification and Testing recommendations

To independently verify the syntax highlighting and hover-card integrations:

1. **BDD Integration Tests (Reqnroll / SpecFlow)**:
   Add test scenarios under `UI/BrainOS.E2E.Tests/` validating FQN coloring and overlay lifecycles:
   ```gherkin
   Scenario: Hovering over an inline FQN in Creator Prompt triggers Hover Card
     Given a live contract catalog is loaded with synapse "Google.Auth.New"
     And the user has input "Verify credentials via Google.Auth.New" inside the Creator Prompt
     When the mouse cursor hovers over the "Google.Auth.New" span in the prompt
     Then an OverlayEntry hover card appears containing "SYNAPSE" and FQN "Google.Auth.New"
     And the overlay is removed when the cursor leaves the span
   ```
2. **Dart / Flutter Widget Tests**:
   Mount `_PromptInputBody` inside a mock `BrainOSClientScope` with a populated catalog and trigger a hover gesture on the highlighted text, validating the insertion of the `OverlayEntry`.
3. **Command Execution**:
   Run E2E test suites inside the workspace terminal:
   ```powershell
   dotnet test UI\BrainOS.E2E.Tests\BrainOS.E2E.Tests.csproj
   ```
