# Detailed Specifications for Milestones 2 & 3: Premium UI Remake

You are the designated Specialist Worker subagent. Your goal is to implement the **Milestones 2 & 3 Premium UI Remake** for the DigitalBrain platform, completely transforming the workspace into a premium, interactive, dark-editorial developer canvas.

---

## Part 1: Architectural Requirements & Invariants

1. **Zero External Library Dependencies**:
   - For all interactive features (node dragging, cable drawing, custom editing controller, particle visualizer), use pure, native Flutter SDK components (`GestureDetector`, `CustomPainter`, `ChangeNotifier`, `TextEditingController`, and standard layout/state widgets). Do NOT add any dependencies to `pubspec.yaml`.
2. **Offline Resilience**:
   - Ensure the UI operates and loads instantly without any network connectivity.
3. **No Cheating / Genuine Implementation**:
   - Fully implement the interactivity. Clicking on the visual canvas must spawn real nodes, dragging ports must draw glowing cables that physically connect nodes, selecting a node must highlight the corresponding InoLang syntax line in the editor, and typing "visualize" in the chat must replace the standby canvas with a live, animated 2D particle/neural simulation.

---

## Part 2: Interactive Visual Neuron Constructor (Left Pane)

### 1. Data Models (`lib/features/neuron_constructor/visual_constructor_models.dart`)
Create or place within a clean architecture file:
```dart
import 'package:flutter/material.dart';

enum NodeKind { neuron, synapse, signal, ingressFilter, egressFilter }

class NodePort {
  final String id;
  final String name;
  final bool isInput;
  Offset relativeOffset;

  NodePort({
    required this.id,
    required this.name,
    required this.isInput,
    required this.relativeOffset,
  });
}

class VisualNode {
  final String id;
  final NodeKind kind;
  String label;
  Offset position;
  Size size;
  final List<NodePort> ports;
  String codePayload; // Underlying expression block / prompt / context

  VisualNode({
    required this.id,
    required this.kind,
    required this.label,
    required this.position,
    this.size = const Size(180, 100),
    required this.ports,
    this.codePayload = '',
  });

  VisualNode copyWith({Offset? position, String? label, String? codePayload}) {
    return VisualNode(
      id: id,
      kind: kind,
      label: label ?? this.label,
      position: position ?? this.position,
      size: size,
      ports: ports,
      codePayload: codePayload ?? this.codePayload,
    );
  }
}

class VisualConnection {
  final String id;
  final String fromPortId;
  final String toPortId;
  final String fromNodeId;
  final String toNodeId;

  VisualConnection({
    required this.id,
    required this.fromPortId,
    required this.toPortId,
    required this.fromNodeId,
    required this.toNodeId,
  });
}
```

### 2. State Management Controller (`lib/features/neuron_constructor/visual_constructor_state.dart`)
Implement a standard `ChangeNotifier` state engine:
- Track a dictionary of active nodes (`Map<String, VisualNode>`).
- Track list of verified connections (`List<VisualConnection>`).
- Maintain viewport translation parameters (`Offset panOffset = Offset.zero`, `double zoomScale = 1.0`).
- Handle mouse/touch drag events to update node coordinates.
- Manage dragging connection cables dynamically:
  - `startDraggingCable(String fromPortId, Offset startPos)`
  - `updateDraggingCable(Offset currentPos)`
  - `cancelDraggingCable()`
  - `completeConnection(String toPortId, String toNodeId)` - removes any prior connection to input `toPortId` to ensure clean one-to-one port lines.

### 3. Canvas & Gesture Engine in Left Pane (`NeuronConstructorView.dart`)
Completely replace the forms inside `NeuronConstructorView` with the dynamic visual editor:
- Wrap workspace in a master `GestureDetector` that updates `panOffset` and `zoomScale` via scale updates (`onScaleUpdate`).
- Draw a retro ambient background grid behind nodes using a custom `GridPainter` aligned to `panOffset` and `zoomScale`.
- Render nodes inside a `Stack` using `Positioned` widgets. For each node widget:
  - Add a dedicated child `GestureDetector` with `onPanUpdate` to enable fluid drag-and-drop movement.
  - Implement a contextual right-click or double-tap radial spawn menu to add nodes: `Neuron`, `Synapse`, `Signal`, `Ingress Filter`, `Egress Filter`.
- Implement `CablePainter` (CustomPainter) to draw gorgeous glowing bezier curves between ports:
  - Draw a neon masked curve with `MaskFilter.blur(BlurStyle.solid, 2.0)` to represent active signal paths.
  - Render a gold line for the active dragging cable.

---

## Part 3: Syntax Highlighted Ino Editor & Two-Way Sync (Right Pane)

### 1. InoSyntaxHighlightEditingController (`lib/features/ino_editor/ino_syntax_highlight_controller.dart`)
Implement a custom `TextEditingController` that overrides `buildTextSpan` to format tokens on the fly using regular expressions:
```dart
import 'package:flutter/material.dart';

class InoSyntaxHighlightEditingController extends TextEditingController {
  static const Color keywordColor = Color(0xFF818CF8);  // Indigo Accent
  static const Color symbolColor = Color(0xFFF43F5E);   // Rose Pink
  static const Color literalColor = Color(0xFF10B981);  // Cyber Emerald
  static const Color commentColor = Color(0xFF6B7280);  // Muted Slate Grey
  static const Color stringColor = Color(0xFFF59E0B);   // Soft Gold
  static const Color normalColor = Color(0xFFC5C9DB);   // Off-white Body

  @override
  TextSpan buildTextSpan({
    required BuildContext context,
    TextStyle? style,
    required bool withComposing,
  }) {
    final List<TextSpan> children = [];
    final rawText = text;

    final RegExp regExp = RegExp(
      r'(?<comment>#.*)|'
      r'(?<string>"(?:[^"\\]|\\.)*"|\x27[^\x27]*\x27)|'
      r'\b(?<keyword>neuron|synapse|on|emit|ask|scenario|given|when|then|let|to|it|ingress|egress|stop)\b|'
      r'(?<symbol>[{}()\[\]@:;,+\-*/=<>!])|'
      r'\b(?<literal>\d+(?:\.\d+)?|true|false|null)\b',
    );

    int lastMatchEnd = 0;
    regExp.allMatches(rawText).forEach((match) {
      if (match.start > lastMatchEnd) {
        children.add(TextSpan(
          text: rawText.substring(lastMatchEnd, match.start),
          style: style?.copyWith(color: normalColor),
        ));
      }

      if (match.namedGroup('comment') != null) {
        children.add(TextSpan(
          text: match.group(0),
          style: style?.copyWith(color: commentColor, fontStyle: FontStyle.italic),
        ));
      } else if (match.namedGroup('string') != null) {
        children.add(TextSpan(
          text: match.group(0),
          style: style?.copyWith(color: stringColor),
        ));
      } else if (match.namedGroup('keyword') != null) {
        children.add(TextSpan(
          text: match.group(0),
          style: style?.copyWith(color: keywordColor, fontWeight: FontWeight.bold),
        ));
      } else if (match.namedGroup('symbol') != null) {
        children.add(TextSpan(
          text: match.group(0),
          style: style?.copyWith(color: symbolColor),
        ));
      } else if (match.namedGroup('literal') != null) {
        children.add(TextSpan(
          text: match.group(0),
          style: style?.copyWith(color: literalColor),
        ));
      }
      lastMatchEnd = match.end;
    });

    if (lastMatchEnd < rawText.length) {
      children.add(TextSpan(
        text: rawText.substring(lastMatchEnd),
        style: style?.copyWith(color: normalColor),
      ));
    }

    return TextSpan(style: style, children: children);
  }
}
```

### 2. Two-Way Sync Logic
- **Visual-to-Code Generation**:
  - Whenever nodes or connections change inside `VisualConstructorState`, auto-synthesize InoLang code (e.g. `neuron MyNeuron ... on synapse(Name) it: ...`) and update the editor controller.
- **Node Highlight Triggers**:
  - When the user selects a node on the visual canvas, search for its signature inside the code text, and set the editor cursor/selection bounds targeting that signature, visually highlighting it.
- **Code-to-Node Reverse Sync**:
  - When typing inside the editor, parse the text (regex check for `on synapse(Name)`) and update the selected node's backing `codePayload`, achieving bidirectional accuracy.

---

## Part 4: Animated 2D Particle Neural Graph (Bottom Canvas Overlay)

1. **Trigger Condition**:
   - When the user types `"visualize invoice-reviewer"` (or just `"visualize"`) in the chat console, set `_isVisualizing = true`.
2. **Animation Loop**:
   - Swap the Markdown display inside `BrainCanvas` with a premium 2D particle simulation canvas (`BrainCanvas2DGraphPainter`).
   - Run a periodic ticking animation using a Flutter `Ticker` or `AnimationController` running at 60fps.
3. **Physics/Rendering Details**:
   - Model orbital coordinates (`NeuralGraphNode`) orbiting a central Core Node with floating radius pulses.
   - Spawn multiple flowing signal particles (`NeuralPathParticle`) moving progressively (`progress += speed * dt`) along bezier connection cables between orbiting nodes.
   - Style the paint using Glowing Gold (`#F59E0B`) and Cyber Teal (`#10B981`) with soft glows.

---

## Part 5: Floating HUD "3D Constellation View" Navigation

1. **HUD Button Widget**:
   - Position a floating rounded cybernetic glassmorphic button styled with `GlassMaterial` (high blur, neon border) in the bottom-right viewport of `ConstructorEditorHomePage`.
2. **Smooth PageRouteBuilder Transition**:
   - Clicking this button transitions to `/brain/digitalbrain`.
   - Override standard routing by establishing a custom `PageRouteBuilder` with `transitionDuration: const Duration(milliseconds: 1400)` that combines:
     1. Scale zoom-out from `0.8` to `1.0` (simulating spatial camera recession).
     2. Exponential opacity fade-in.
3. **Orbital Camera Alignment on Landing**:
   - When landing on `/brain/digitalbrain` (the 3D Constellation view), interpolate the virtual WebGL/scene camera from its high-orbit zoom vector to focus on the target node's coordinates over `1200ms` using smooth dampening.

---

## Implementation & Handoff Deliverables

1. Fully implement all changes under `UI/flutter/lib/`.
2. Run `flutter analyze` inside `UI/flutter` to verify that all modified/added files are completely clean and have 0 issues or warnings.
3. Run `dotnet build DigitalBrain.slnx` at the workspace root to ensure C# backend and Flutter UI compile and build cleanly with 0 errors.
4. Document all changes made, detailed architectures, compilation results, and visual verification instructions in `e:\digitalbrain\.agents\worker_m2_3\handoff.md`.
5. Send a handoff message to me (the parent Project Orchestrator) with the report path.
