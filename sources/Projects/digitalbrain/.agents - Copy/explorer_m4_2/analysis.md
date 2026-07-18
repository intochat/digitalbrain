# Analysis Report: Syntax Highlighting & Inline Signature Hover Cards

## Executive Summary
This report analyzes the end-to-end mechanism for syntax highlighting and inline signature hover cards driven by the catalog contract registry (`.ino-catalog.json`) and the `Introspector` service within the DigitalBrain Flutter UI editor and C# Kernel. We present direct evidence of how the catalog load, real-time coloring, and hover card rendering are currently architected, followed by concrete, low-risk recommendations to fully enable dynamic kind-based colorization and support schema-level contract overloads in the Flutter UI.

---

## 1. Catalog Load Architecture

### 1.1 The gRPC Introspector Service Query Pipeline
The Flutter code editor loads the active schema catalog asynchronously from the backend `IntrospectorNeuron` service using the standard gRPC message-passing path (via `SynapseEnvelope` wrappers). This avoids introducing specialized gRPC endpoints and maintains a unified, secure gateway contract:

1. **Client-Side Trigger (`didChangeDependencies`)**:
   In `brainos_rfw_library.dart` (lines 1816–1822), when the editor widget is initialized or its dependencies change, it invokes `_loadCatalog()` if it hasn't loaded yet.
2. **gRPC Request Packaging & Dispatch (`_loadCatalog()`)**:
   In `brainos_rfw_library.dart` (lines 1824–1842), the editor:
   - Obtains the `BrainOSGatewayClient` via `BrainOSClientScope.of(context)`.
   - Constructs a JSON payload representing `BrainOS.Kernel.Contracts.Introspector.QueryCatalogContractsRequest` with the appropriate fields (e.g. setting `ReceiverNeuronType = 'IntrospectorNeuron'`).
   - Packages this JSON string into a UTF-8 byte array (`Uint8List`) inside a `SynapseEnvelope`.
   - Calls `client.send(envelope)` asynchronously.
3. **Kernel-Side Handling (`IntrospectorNeuron.cs`)**:
   In C# `IntrospectorNeuron.cs` (lines 260–284), the silo receives the `QueryCatalogContractsRequest` case:
   - It queries the injected `IContractCatalog catalog` by calling `catalog.GetAllSchemas()`.
   - The schemas are mapped into `CatalogContractSchema` records.
   - It instantiates a `QueryCatalogContractsResponse` carrying the list of mapped schemas and dispatches it back to the caller.
4. **Client-Side Response Processing & Storage**:
   In `brainos_rfw_library.dart` (lines 1844–1861), the editor awaits the response envelope, decodes the payload to UTF-8, decodes the JSON array, and deserializes each entry into a `CatalogContractSchema` Dart class:
   ```dart
   class CatalogContractSchema {
     final String fqn;
     final int kind; // 0 = Synapse, 1 = Signal, 2 = Neuron
     final List<String> fields;
     ...
   }
   ```
   Finally, it updates the state `_catalog = loaded;`, triggering a widget rebuild to refresh highlighting and autocompletion data.

### 1.2 Catalog Fail-Safe Tolerance (TC-2.3-1)
If the backend introspector query fails (e.g., due to network disconnection or silo failure), the `_loadCatalog()` method catches the exception (line 1858), logs it gracefully (`debugPrint('Failed to load contract catalog: $e')`), and ensures the UI does **not** crash. 

The editor operates safely with an empty `_catalog` list because:
- Lookups in autocompletion, highlighting, and hover handlers fail-safe by utilizing defensive methods (`orElse`, `any`, and null-checks) which default to safe, generic behaviors.
- We recommend strengthening this with an **offline fallback**. When gRPC client query fails or is not present, the editor should attempt to load a cached copy of the catalog from local assets (e.g., `AssetManifest` pointing to a local `.ino-catalog.json` or `.agents/` resource):
  ```dart
  try {
    final response = await client.send(envelope);
    // ...
  } catch (e) {
    debugPrint('Failed to load contract catalog from gRPC: $e. Attempting local assets fallback.');
    try {
      final jsonStr = await DefaultAssetBundle.of(context).loadString('assets/ino-catalog.json');
      // Parse local JSON catalog file...
    } catch (assetErr) {
      debugPrint('Local assets fallback failed: $assetErr');
    }
  }
  ```

---

## 2. Real-Time Syntax Highlighting

### 2.1 The Custom editing Controller
Real-time syntax highlighting is driven directly inside the text editing pane via `InoLangTextEditingController` (extending `TextEditingController`), specifically within `buildTextSpan` (lines 1277–1388 in `brainos_rfw_library.dart`). This method utilizes a global `RegExp` to identify tokens as they are typed:

```dart
final RegExp regex = RegExp(
  r'(//.*)|' // Group 1: Comments
  r'("[^"]*")|' // Group 2: Strings
  r'(\b(?:on|synapse|signal|neuron|instance|ask|to|for|emit|given|returns|when|then|every|any|no|has|emitted|let|save|into|count|counter|scenario)\b)|' // Group 3: Keywords
  r'(\bit\b)|' // Group 4: Pronoun 'it'
  r'(\b[a-zA-Z_]\w*(?:\.[a-zA-Z_]\w*)+\b)|' // Group 5: FQNs
  r'(\b\d+\b)|' // Group 6: Numbers
  r'(\b(?:using|namespace|public|sealed|record|class|struct|interface|get|set|init|private|protected|internal|override|virtual|async|await|return|string|int|var|bool|void)\b)|' // Group 7: C# Keywords
  r'(\[[a-zA-Z_]\w*\])' // Group 8: Attributes
);
```

### 2.2 Kind-Based Colorization Defect
Currently, inside `buildTextSpan` (lines 1340–1352), Group 5 matches dotted FQNs and styles all of them with a static color `BrainOSColors.goldSoft` regardless of their actual kind (neuron, synapse, signal):

```dart
} else if (match.group(5) != null) {
  // Dotted FQN (e.g. DB.Google.Auth)
  final fqn = match.group(5)!;
  spans.add(TextSpan(
    text: fqn,
    style: defaultStyle.copyWith(
      color: BrainOSColors.goldSoft, // <-- Mismatched: Hardcoded
      fontWeight: FontWeight.bold,
    ),
    mouseCursor: SystemMouseCursors.click,
    onEnter: (event) => onHoverEnter?.call(fqn, event),
    onExit: (event) => onHoverExit?.call(event),
  ));
}
```

### 2.3 Proposed Solution
To support real-time kind-based highlighting, we must supply the controller with the loaded `_catalog` list. 
1. **Pass Catalog Getter to Controller**:
   Modify the `InoLangTextEditingController` constructor to accept a catalog resolver:
   ```dart
   class InoLangTextEditingController extends TextEditingController {
     InoLangTextEditingController({
       super.text,
       this.onHoverEnter,
       this.onHoverExit,
       required List<CatalogContractSchema> Function() getCatalog,
     }) : _getCatalog = getCatalog;

     final List<CatalogContractSchema> Function() _getCatalog;
     ...
   ```
2. **Highlight Based on Kind**:
   Lookup the FQN in the catalog inside `buildTextSpan`:
   ```dart
   } else if (match.group(5) != null) {
     final fqn = match.group(5)!;
     final catalog = _getCatalog();
     final schema = catalog.firstWhere(
       (s) => s.fqn.toLowerCase() == fqn.toLowerCase(),
       orElse: () => CatalogContractSchema(fqn: '', kind: -1, fields: []),
     );

     Color fqnColor = BrainOSColors.goldSoft; // Fallback
     if (schema.kind == 0) fqnColor = BrainOSColors.tealSoft;    // Synapse
     if (schema.kind == 1) fqnColor = BrainOSColors.goldSoft;    // Signal
     if (schema.kind == 2) fqnColor = BrainOSColors.violetSoft;  // Neuron

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

## 3. Inline Signature Hover Cards

### 3.1 Pointer Hover Mechanism
The custom editor leverages Flutter's standard `Overlay` system to display interactive signature hover cards dynamically:
- When a user hovers the cursor over a dotted FQN span inside `InoLangTextEditingController.buildTextSpan`, the `onEnter` callback fires, passing the matched FQN and the `PointerEnterEvent`.
- `_CodeEditorBodyState._showHoverCard(fqn, position)` is invoked (line 1459).
- It performs a fast lookup in `_catalog` for the FQN, retrieves the kind and fields, builds a blur-filtered (`BackdropFilter`) material panel containing FQN details, and inserts it as an `OverlayEntry` via `Overlay.of(context).insert(...)`.
- When the mouse cursor leaves the span, `onExit` triggers `_hideHoverCard()`, removing the entry cleanly from the overlay.

### 3.2 Overload Resolution Enhancement
To achieve full compatibility with the **typed .NET record overloads** specified in proposal §19.5, the current single-entry lookup must be enhanced. 

1. **The Overload Representation in Catalog**:
   Rather than treating the catalog as a single schema per FQN, allow multiple schemas with the same FQN (or support an `overloads` nested array as detailed in §19.5).
   If mapping directly into the current schema representation, multiple schemas with the same Fqn will reside in `_catalog` with differing fields.
2. **Look Up All Overloads in Hover Card**:
   Instead of `_catalog.firstWhere`, retrieve *all* matches:
   ```dart
   final overloads = _catalog.where(
     (s) => s.fqn.toLowerCase() == fqn.toLowerCase()
   ).toList();
   ```
   If `overloads` is empty, return immediately.
3. **Format Multiple Overloads in UI**:
   Renders a card outlining the total overloads count and lists the field shapes for each overload cleanly, separated by dividers:
   ```dart
   // Inside OverlayEntry builder
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
             child: Text(kindName, ...),
           ),
           const SizedBox(width: 8),
           Text(
             fqn,
             style: GoogleFonts.jetBrainsMono(fontWeight: FontWeight.bold, color: BrainOSColors.ink),
           ),
         ],
       ),
       const SizedBox(height: 4),
       Text(
         '${overloads.length} overload(s)',
         style: GoogleFonts.outfit(fontSize: 9, color: BrainOSColors.inkLow),
       ),
       for (int i = 0; i < overloads.length; i++) ...[
         const SizedBox(height: 6),
         Divider(color: Colors.white.withValues(alpha: 0.1), height: 1),
         const SizedBox(height: 6),
         Text(
           'overload #${i + 1}',
           style: GoogleFonts.jetBrainsMono(fontSize: 8, color: BrainOSColors.inkLow),
         ),
         for (final field in overloads[i].fields)
           Padding(
             padding: const EdgeInsets.only(left: 8.0, top: 2.0),
             child: Text(
               '• $field',
               style: GoogleFonts.jetBrainsMono(fontSize: 10, color: BrainOSColors.inkMid),
             ),
           ),
       ]
     ],
   )
   ```

---

## 4. Logic Chain & Evidence Table

The following table collects the exact file locations, parsed code references, and logical inferences that build our verification chain:

| File Path | Line(s) | Observed Content / Context | Logical Inference |
|---|---|---|---|
| `kernel/BrainOS.Kernel.Contracts/Introspector/QueryCatalogContractsRequest.cs` | 7–13 | `public sealed record QueryCatalogContractsRequest(...)` | Defines the standard C# record wrapper for querying the introspector cluster-wide. |
| `kernel/BrainOS.Kernel.Contracts/Introspector/QueryCatalogContractsResponse.cs` | 7–30 | `CatalogContractKind` (Synapse, Signal, Neuron), `CatalogContractSchema` (Fqn, Kind, Fields) | Outlines how contract metadata is represented inside the grain contracts, using integer kinds 0, 1, 2. |
| `kernel/BrainOS.Kernel/Introspector/IntrospectorNeuron.cs` | 260–284 | `case QueryCatalogContractsRequest req:` queries `catalog.GetAllSchemas()` | Confirms C# silo service dynamically resolves all schemas from `IContractCatalog` and packages them into the response. |
| `UI/flutter/lib/features/rfw_gallery/brainos_rfw_library.dart` | 1824–1861 | `_loadCatalog()` sends gRPC `QueryCatalogContractsRequest` envelope. | Confirms Flutter editor constructs a `QueryCatalogContractsRequest` payload and posts it over standard gRPC. |
| `UI/flutter/lib/features/rfw_gallery/brainos_rfw_library.dart` | 1288–1388 | `InoLangTextEditingController.buildTextSpan` styles `match.group(5)` (FQNs) with `goldSoft`. | Highlights the colorization bottleneck: it lacks access to the loaded catalog to styles FQNs uniquely by kind. |
| `UI/flutter/lib/features/rfw_gallery/brainos_rfw_library.dart` | 1459–1589 | `_showHoverCard(fqn, position)` fetches first schema match and shows an `OverlayEntry`. | Identifies the hover card rendering entrypoint, proving we can display multiple overloads by retrieving all matches. |
| `docs/v3/2026-05-21-inolang-roslyn-meta-language.md` | 602–653 | §19.5 details overloads with same FQN base and named arg resolving. | Establishes the target overload catalog shape where `Acme.Submit` has multiple signatures. |

---

## 5. Verification & Testing Recommendations

To safely verify syntax highlighting and hover cards:

1. **BDD Scenario Tests (Reqnroll)**:
   Add scenarios inside `UI/BrainOS.E2E.Tests/` targeting the editor highlight behavior:
   ```gherkin
   Scenario: FQN color matches contract kind from catalog
     Given a live contract catalog contains a synapse "Acme.Submit" and a neuron "Acme.Worker"
     When a user inputs text "ask Acme.Worker to emit signal(Acme.Submit)" inside the code editor
     Then the editor span for "Acme.Worker" is highlighted as a neuron color "violetSoft"
     And the editor span for "Acme.Submit" is highlighted as a synapse color "tealSoft"
   ```
2. **Fast Unit Tests (xUnit)**:
   Verify the offline asset parser handles missing or empty `.ino-catalog.json` smoothly in `IntrospectorNeuronTests.cs` (e.g. `CatalogLoadFailTolerance` TC-2.3-1).
3. **Execution Command**:
   Run the full system test suite via PowerShell:
   ```powershell
   dotnet test UI\BrainOS.E2E.Tests\BrainOS.E2E.Tests.csproj
   ```
