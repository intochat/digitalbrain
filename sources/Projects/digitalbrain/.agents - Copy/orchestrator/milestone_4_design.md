# Technical Design & Implementation Plan: Milestone 4 (InoLang Editor & Syntax Highlighting)

This design document outlines the comprehensive engineering plan to fulfill all requirements of **Milestone 4 (InoLang Editor & Syntax Highlighting)** under the DigitalBrain Production Readiness initiative. This blueprint is synthesized from the deep codebase analysis provided by three independent Explorer subagents.

---

## 1. Architectural Blueprint & Shared State

To prevent redundant backend queries and enable cross-widget synchronization, we introduce a centralized catalog caching singleton inside the Flutter UI client workspace.

```
                  ┌──────────────────────────────┐
                  │      BrainOSGatewayClient    │
                  └──────────────┬───────────────┘
                                 │ gRPC Query Catalog
                                 ▼
                  ┌──────────────────────────────┐
                  │    BrainOSCatalogManager     │ (Singleton Cache)
                  └──────┬────────────────┬──────┘
                         │                │
       Synchronous Query │                │ Synchronous Query
                         ▼                ▼
         ┌───────────────────────┐ ┌─────────────────────────┐
         │ InoLangTextController │ │  PromptTextController   │
         └───────────┬───────────┘ └────────────┬────────────┘
                     │                          │
                     ▼                          ▼
               [.ino Editor]             [Creator Prompt]
         (Kind-Based Highlights)    (Plain-English Highlights)
         (Glassmorphic Overlays)    (Shared Hover overlays)
```

### 1.1 `BrainOSCatalogManager` Singleton
- **Purpose**: Manage the thread-safe loading, local assets fallback, and active caching of catalog schemas (`CatalogContractSchema` instances) representing Synapses (`0`), Signals (`1`), and Neurons (`2`).
- **Location**: `UI/flutter/lib/features/rfw_gallery/brainos_rfw_library.dart` (or imported).
- **Interface**:
  ```dart
  class BrainOSCatalogManager {
    BrainOSCatalogManager._internal();
    static final BrainOSCatalogManager instance = BrainOSCatalogManager._internal();

    List<CatalogContractSchema> _cachedCatalog = [];
    bool _loaded = false;

    List<CatalogContractSchema> get catalog => _cachedCatalog;
    bool get isLoaded => _loaded;

    Future<List<CatalogContractSchema>> ensureLoaded(BuildContext context) async {
      if (_loaded) return _cachedCatalog;
      await reload(context);
      return _cachedCatalog;
    }

    Future<void> reload(BuildContext context) async {
      final client = BrainOSClientScope.of(context);
      final envelope = SynapseEnvelope()
        ..typeName = 'BrainOS.Kernel.Contracts.Introspector.QueryCatalogContractsRequest'
        ..payload = utf8.encode(jsonEncode({}));
      
      try {
        final response = await client.send(envelope);
        final jsonArray = jsonDecode(utf8.decode(response.payload)) as List;
        _cachedCatalog = jsonArray.map((s) => CatalogContractSchema.fromJson(s as Map<String, dynamic>)).toList();
        _loaded = true;
      } catch (e) {
        debugPrint('gRPC Catalog load failed: $e. Attempting local assets fallback.');
        try {
          final jsonStr = await DefaultAssetBundle.of(context).loadString('assets/ino-catalog.json');
          final jsonArray = jsonDecode(jsonStr) as List;
          _cachedCatalog = jsonArray.map((s) => CatalogContractSchema.fromJson(s as Map<String, dynamic>)).toList();
          _loaded = true;
        } catch (assetErr) {
          debugPrint('Local assets fallback failed: $assetErr');
          _cachedCatalog = []; // Suppress crash, operate empty
        }
      }
    }
  }
  ```

---

## 2. InoLang Editor Kind-Based Highlighting

Currently, the `InoLangTextEditingController.buildTextSpan` colors all dotted FQNs with `goldSoft` (Group 5). We refactor colorization to look up the FQN kind in the cached catalog manager.

### 2.1 Color Rules
- **Synapses (`kind == 0`)**: `BrainOSColors.tealSoft` (Teal)
- **Signals (`kind == 1`)**: `BrainOSColors.goldSoft` (Gold)
- **Neurons (`kind == 2`)**: `BrainOSColors.violetSoft` (Violet/Purple)
- **Fallback / Unresolved**: `BrainOSColors.goldSoft` (Gold, bold click pointer)

### 2.2 Controller Refactoring
Update `InoLangTextEditingController` to query the singleton catalog:
```dart
} else if (match.group(5) != null) {
  final fqn = match.group(5)!;
  final schema = BrainOSCatalogManager.instance.catalog.firstWhere(
    (s) => s.fqn.toLowerCase() == fqn.toLowerCase(),
    orElse: () => CatalogContractSchema(fqn: '', kind: -1, fields: []),
  );

  Color fqnColor = BrainOSColors.goldSoft;
  if (schema.kind == 0) fqnColor = BrainOSColors.tealSoft;
  if (schema.kind == 1) fqnColor = BrainOSColors.goldSoft;
  if (schema.kind == 2) fqnColor = BrainOSColors.violetSoft;

  spans.add(TextSpan(
    text: fqn,
    style: defaultStyle.copyWith(
      color: fqnColor,
      fontWeight: FontWeight.bold,
    ),
    mouseCursor: SystemMouseCursors.click,
    onEnter: (event) => onHoverEnter?.call(fqn, event),
    onExit: (event) => onHoverExit?.call(event),
  ));
}
```

---

## 3. Plain English FQN Parsing (Creator Prompt Pane)

To satisfy Requirement **R3**, the plain English Creator Prompt input widget must parse and highlight inline FQNs (like `Google.Auth.New` or `DigitalBrain.SDK.*`) dynamically.

### 3.1 `PromptTextEditingController`
Replace the basic `TextEditingController` inside `_PromptInputBodyState` (lines 2363–2390) with a custom controller:
```dart
class PromptTextEditingController extends TextEditingController {
  PromptTextEditingController({super.text, this.onHoverEnter, this.onHoverExit});

  final Function(String fqn, PointerEnterEvent event)? onHoverEnter;
  final Function(PointerExitEvent event)? onHoverExit;

  @override
  TextSpan buildTextSpan({required BuildContext context, TextStyle? style, required bool withComposing}) {
    final defaultStyle = style ?? const TextStyle();
    final List<TextSpan> spans = [];

    // Captures dotted word tokens (FQNs) and wildcarded assemblies (DigitalBrain.SDK.*)
    final RegExp regex = RegExp(
      r'(\b[a-zA-Z_]\w*(?:\.[a-zA-Z_]\w*)*(?:\.\*)?\b)'
    );

    int lastIndex = 0;
    text.splitMapJoin(
      regex,
      onMatch: (Match match) {
        final token = match.group(0)!;
        final cleanFqn = token.endsWith('.*') ? token.substring(0, token.length - 2) : token;
        
        final isResolved = BrainOSCatalogManager.instance.catalog.any(
          (s) => s.fqn.toLowerCase().startsWith(cleanFqn.toLowerCase())
        );

        if (isResolved) {
          spans.add(TextSpan(
            text: token,
            style: defaultStyle.copyWith(
              color: BrainOSColors.goldSoft,
              fontWeight: FontWeight.bold,
              decoration: TextDecoration.underline,
            ),
            mouseCursor: SystemMouseCursors.click,
            onEnter: (event) => onHoverEnter?.call(cleanFqn, event),
            onExit: (event) => onHoverExit?.call(event),
          ));
        } else {
          spans.add(TextSpan(text: token, style: defaultStyle));
        }
        return '';
      },
      onNonMatch: (String nonMatch) {
        spans.add(TextSpan(text: nonMatch, style: defaultStyle));
        return '';
      }
    );

    return TextSpan(children: spans, style: defaultStyle);
  }
}
```

---

## 4. Glassmorphic Overload Hover Cards

To support signature overloads, the hover card lookup must query *all* schemas that match the hovered FQN base, displaying structural parameter differences inside Outlined lists.

### 4.1 Card Overlay Update (`_showHoverCard`)
Modify `_showHoverCard(String fqn, Offset position, BuildContext context)` in both prompt and code controllers to render a comprehensive schema card:
```dart
void _showHoverCard(String fqn, Offset position, BuildContext context) {
  _hideHoverCard();

  final overloads = BrainOSCatalogManager.instance.catalog.where(
    (s) => s.fqn.toLowerCase() == fqn.toLowerCase()
  ).toList();

  if (overloads.isEmpty) return;
  final schema = overloads.first; // Representative

  Color accentColor = BrainOSColors.goldSoft;
  String kindName = 'SIGNAL';
  if (schema.kind == 0) { accentColor = BrainOSColors.tealSoft; kindName = 'SYNAPSE'; }
  if (schema.kind == 2) { accentColor = BrainOSColors.violetSoft; kindName = 'NEURON'; }

  _hoverEntry = OverlayEntry(
    builder: (context) => Positioned(
      left: position.dx,
      top: position.dy + 24,
      child: Material(
        color: Colors.transparent,
        child: ClipRRect(
          borderRadius: BorderRadius.circular(12),
          child: BackdropFilter(
            filter: ImageFilter.blur(sigmaX: 10, sigmaY: 10),
            child: Container(
              padding: const EdgeInsets.all(12),
              width: 320,
              decoration: BoxDecoration(
                color: Colors.black.withValues(alpha: 0.75),
                borderRadius: BorderRadius.circular(12),
                border: Border.all(color: accentColor.withValues(alpha: 0.3)),
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
                        child: Text(kindName, style: GoogleFonts.outfit(fontSize: 9, color: accentColor, fontWeight: FontWeight.bold)),
                      ),
                      const SizedBox(width: 8),
                      Expanded(
                        child: Text(
                          fqn,
                          style: GoogleFonts.jetBrainsMono(fontWeight: FontWeight.bold, color: Colors.white, fontSize: 11),
                          overflow: TextOverflow.ellipsis,
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: 6),
                  Text('${overloads.length} signature overload(s) registered:', style: GoogleFonts.outfit(fontSize: 9, color: Colors.grey[400])),
                  for (int i = 0; i < overloads.length; i++) ...[
                    const SizedBox(height: 6),
                    const Divider(color: Colors.white12, height: 1),
                    const SizedBox(height: 6),
                    Text('signature #${i + 1}', style: GoogleFonts.jetBrainsMono(fontSize: 8, color: Colors.grey[500])),
                    const SizedBox(height: 4),
                    if (overloads[i].fields.isEmpty)
                      Text('• (no payload fields)', style: GoogleFonts.jetBrainsMono(fontSize: 9, color: Colors.grey[400]))
                    else
                      for (final field in overloads[i].fields)
                        Padding(
                          padding: const EdgeInsets.only(left: 8.0, top: 2.0),
                          child: Text('• $field', style: GoogleFonts.jetBrainsMono(fontSize: 10, color: Colors.grey[300])),
                        ),
                  ]
                ],
              ),
            ),
          ),
        ),
      ),
    ),
  );

  Overlay.of(context).insert(_hoverEntry!);
}
```

---

## 5. Synapse Tab & Dynamic Code Extraction

### 5.1 Outbound Emitted Signals Scanning
The editor should scan the `.ino` text not only for `on synapse(...)` blocks but also for outbound emitted signals (`emit signal(...)` or `fire synapse(...)`).
- **Dynamic Signal Scanning**:
  ```dart
  List<String> _extractEmittedSignals(String code) {
    final regex = RegExp(r'\b(?:emit\s+signal|fire\s+synapse)\s*\(\s*(DB\.[a-zA-Z_]\w*(?:\.[a-zA-Z_]\w*)*)\s*\)');
    return regex.allMatches(code).map((m) => m.group(1)!).toSet().toList();
  }
  ```
- **RFW Synapses Tab Integration**:
  Feed the lists of `synapses` and `emittedSignals` to RFW.
  Under tab 3 ("Synapses") in RFW layout, render:
  - **Inbound synapse Triggers**: Renders `SynapseRow` with `onFire` triggers that dispatch a visual pulse across the 3D scene.
  - **Outbound Emitted Signals**: Renders `SignalRow` with test-emission triggers that raise snackbars.

---

## 6. Production Orleans Staging & gRPC Compilation

Replace simulated delay compilation with a real gRPC backend compile and stage action:

1. **Construct Compile Envelope**:
   ```dart
   final requestPayload = jsonEncode({
     'neuronId': _zoomedNeuron,
     'sourceCode': _textController.text,
     'optimizationLevel': 'Release',
     'enableRoslynScripting': true,
     'testDrivenLoopMode': true,
   });

   final envelope = SynapseEnvelope()
     ..typeName = 'BrainOS.Kernel.Contracts.Compiler.CompileNeuronRequest'
     ..payload = utf8.encode(requestPayload);
   ```
2. **Submit to Gateway Client**:
   ```dart
   try {
     final response = await client.send(envelope);
     final result = jsonDecode(utf8.decode(response.payload));
     setState(() {
       if (result['success'] == true) {
         _compileStatus = 'success';
         _csharpSource = result['generatedCsharp'] ?? '';
         _compileErrors = [];
       } else {
         _compileStatus = 'error';
         _compileErrors = List<String>.from(result['errors'] ?? []);
       }
     });
   } catch (e) {
     setState(() {
       _compileStatus = 'error';
       _compileErrors = ['gRPC compilation request failed: $e'];
     });
   }
   ```
3. **Diagnostics console**:
   When `_compileStatus == 'error'`, standard Flutter diagnostic banner `_buildCompileDiagnosticsConsole` immediately renders translucent error sheets displaying parsed compiler exceptions in JetBrains Mono.

---

## 7. Verification Gates

1. **Compile & Build Pass**: The whole Flutter UI workspace and C# Kernel projects must build successfully.
2. **BDD Scenario Coverage**:
   - `Scenario: FQN syntax highlighting color matches catalog kinds`
   - `Scenario: Hover cards render overload field lists for duplicated FQNs`
   - `Scenario: Catalog load falls back gracefully when gRPC introspector fails`
3. **Audited Verification**: Forensic Auditor validates zero resource leaks, clean memory operations, and authentic schema rendering.
