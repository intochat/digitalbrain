import 'package:flutter/material.dart';
import 'visual_constructor_models.dart';

class VisualConstructorState extends ChangeNotifier {
  final Map<String, VisualNode> _nodes = {};
  final List<VisualConnection> _connections = [];

  // S6 Hierarchy tracking
  final List<String> _drillPath = [];
  final Map<String, Map<String, VisualNode>> _subNodes = {};
  final Map<String, List<VisualConnection>> _subConnections = {};

  List<String> get drillPath => _drillPath;

  Offset _panOffset = Offset.zero;
  double _zoomScale = 1.0;

  Offset get panOffset => _panOffset;
  set panOffset(Offset val) {
    if (_panOffset != val) {
      _panOffset = val;
      notifyListeners();
    }
  }

  double get zoomScale => _zoomScale;
  set zoomScale(double val) {
    if (_zoomScale != val) {
      _zoomScale = val;
      notifyListeners();
    }
  }

  // Active drag cable state
  String? dragFromPortId;
  String? dragFromNodeId;
  Offset? dragStartPos;
  Offset? dragCurrentPos;

  // Selection state
  String? selectedNodeId;

  // Callback to sync code changes
  void Function(String code)? onCodeGenerated;

  // Callback to trigger visual animation pulses locally
  void Function(String typeName)? onSynapseFiredLocally;

  // Dynamically resolve nodes/connections based on the drill path
  Map<String, VisualNode> get nodes {
    if (_drillPath.isEmpty) return _nodes;
    final activeId = _drillPath.last;
    return _subNodes[activeId] ?? {};
  }

  List<VisualConnection> get connections {
    if (_drillPath.isEmpty) return _connections;
    final activeId = _drillPath.last;
    return _subConnections[activeId] ?? [];
  }

  // S6 hierarchy control methods
  void drillInto(String nodeId) {
    final targetNode = nodes[nodeId];
    if (targetNode != null && targetNode.kind == NodeKind.neuron) {
      _drillPath.add(nodeId);
      // Lazily initialize nested collections if they don't exist
      _subNodes.putIfAbsent(nodeId, () => {});
      _subConnections.putIfAbsent(nodeId, () => []);
      selectedNodeId = null;
      notifyListeners();
      _triggerSync();
    }
  }

  void drillPop() {
    if (_drillPath.isNotEmpty) {
      _drillPath.removeLast();
      selectedNodeId = null;
      notifyListeners();
      _triggerSync();
    }
  }

  void drillBackTo(int index) {
    if (index == -1) {
      _drillPath.clear();
    } else if (index < _drillPath.length) {
      _drillPath.removeRange(index + 1, _drillPath.length);
    }
    selectedNodeId = null;
    notifyListeners();
    _triggerSync();
  }

  VisualConstructorState() {
    // Add the two LLM-backed neurons pre-wired together by default
    final translationNode = VisualNode(
      id: 'llm_translation_neuron',
      kind: NodeKind.neuron,
      label: 'LlmTranslationNeuron',
      position: const Offset(120, 180),
      ports: [
        NodePort(id: 'translation_in', name: 'In', isInput: true, relativeOffset: const Offset(0, 50)),
        NodePort(id: 'translation_out', name: 'Out', isInput: false, relativeOffset: const Offset(180, 50)),
      ],
      codePayload: 'on TranslateTextRequest it:\n  let translated = ask IChatClient to translate\n  emit TextTranslatedEvent',
    );

    final alertingNode = VisualNode(
      id: 'llm_alerting_neuron',
      kind: NodeKind.neuron,
      label: 'LlmAlertingNeuron',
      position: const Offset(480, 180),
      ports: [
        NodePort(id: 'alerting_in', name: 'In', isInput: true, relativeOffset: const Offset(0, 50)),
        NodePort(id: 'alerting_out', name: 'Out', isInput: false, relativeOffset: const Offset(180, 50)),
      ],
      codePayload: 'on TextTranslatedEvent it:\n  let sentiment = ask IChatClient to analyze\n  emit SystemAlertFiredEvent',
    );

    _nodes[translationNode.id] = translationNode;
    _nodes[alertingNode.id] = alertingNode;

    // Connect LlmTranslationNeuron to LlmAlertingNeuron with an active synapse connection
    _connections.add(VisualConnection(
      id: 'active_synapse_wire',
      fromNodeId: 'llm_translation_neuron',
      fromPortId: 'translation_out',
      toNodeId: 'llm_alerting_neuron',
      toPortId: 'alerting_in',
    ));
  }

  void addNode(VisualNode node) {
    if (_drillPath.isEmpty) {
      _nodes[node.id] = node;
    } else {
      final activeId = _drillPath.last;
      _subNodes.putIfAbsent(activeId, () => {})[node.id] = node;
    }
    notifyListeners();
    _triggerSync();
  }

  void removeNode(String id) {
    if (_drillPath.isEmpty) {
      _nodes.remove(id);
      _connections.removeWhere((conn) => conn.fromNodeId == id || conn.toNodeId == id);
    } else {
      final activeId = _drillPath.last;
      _subNodes[activeId]?.remove(id);
      _subConnections[activeId]?.removeWhere((conn) => conn.fromNodeId == id || conn.toNodeId == id);
    }
    if (selectedNodeId == id) {
      selectedNodeId = null;
    }
    notifyListeners();
    _triggerSync();
  }

  void selectNode(String? id) {
    selectedNodeId = id;
    notifyListeners();
  }

  VisualNode? _getActiveNode(String id) {
    if (_drillPath.isEmpty) return _nodes[id];
    return _subNodes[_drillPath.last]?[id];
  }

  void _setActiveNode(String id, VisualNode node) {
    if (_drillPath.isEmpty) {
      _nodes[id] = node;
    } else {
      _subNodes[_drillPath.last]?[id] = node;
    }
  }

  void updateNodePosition(String id, Offset delta) {
    final node = _getActiveNode(id);
    if (node != null) {
      _setActiveNode(id, node.copyWith(position: node.position + delta));
      notifyListeners();
    }
  }

  void setNodePosition(String id, Offset position) {
    final node = _getActiveNode(id);
    if (node != null) {
      _setActiveNode(id, node.copyWith(position: position));
      notifyListeners();
    }
  }

  void updateNodePayload(String id, String payload) {
    final node = _getActiveNode(id);
    if (node != null) {
      _setActiveNode(id, node.copyWith(codePayload: payload));
      notifyListeners();
      _triggerSync();
    }
  }

  void updateNodeLabel(String id, String label) {
    final node = _getActiveNode(id);
    if (node != null) {
      _setActiveNode(id, node.copyWith(label: label));
      notifyListeners();
      _triggerSync();
    }
  }

  // Dragging connection cables
  void startDraggingCable(String fromNodeId, String fromPortId, Offset startPos) {
    dragFromNodeId = fromNodeId;
    dragFromPortId = fromPortId;
    dragStartPos = startPos;
    dragCurrentPos = startPos;
    notifyListeners();
  }

  void updateDraggingCable(Offset currentPos) {
    dragCurrentPos = currentPos;
    notifyListeners();
  }

  void cancelDraggingCable() {
    dragFromNodeId = null;
    dragFromPortId = null;
    dragStartPos = null;
    dragCurrentPos = null;
    notifyListeners();
  }

  void completeConnection(String toNodeId, String toPortId) {
    if (dragFromNodeId == null || dragFromPortId == null) return;

    if (_drillPath.isEmpty) {
      _connections.removeWhere((conn) => conn.toPortId == toPortId);
    } else {
      _subConnections[_drillPath.last]?.removeWhere((conn) => conn.toPortId == toPortId);
    }

    final newConn = VisualConnection(
      id: 'conn_${DateTime.now().millisecondsSinceEpoch}',
      fromNodeId: dragFromNodeId!,
      fromPortId: dragFromPortId!,
      toNodeId: toNodeId,
      toPortId: toPortId,
    );

    if (_drillPath.isEmpty) {
      _connections.add(newConn);
    } else {
      _subConnections.putIfAbsent(_drillPath.last, () => []).add(newConn);
    }

    cancelDraggingCable();
    notifyListeners();
    _triggerSync();
  }

  void _triggerSync() {
    if (onCodeGenerated != null) {
      onCodeGenerated!(generateInoCode());
    }
  }

  String generateInoCode() {
    final sb = StringBuffer();
    sb.writeln('neuron DigitalBrain.Custom.InvoiceReviewer');
    sb.writeln('  "Automated invoice reviewer designed by Operator."');
    sb.writeln();

    final activeNodes = nodes;
    final activeConnections = connections;

    final ingressFilters = activeNodes.values.where((n) => n.kind == NodeKind.ingressFilter).toList();
    final egressFilters = activeNodes.values.where((n) => n.kind == NodeKind.egressFilter).toList();
    final synapses = activeNodes.values.where((n) => n.kind == NodeKind.synapse).toList();

    if (ingressFilters.isNotEmpty) {
      sb.writeln('  # --- Layer 1: Inbound Grain Call Filter ---');
      sb.writeln('  on ingress:');
      for (var node in ingressFilters) {
        final payload = node.codePayload.trim();
        final code = payload.isNotEmpty ? payload : 'stop';
        for (var line in code.split('\n')) {
          sb.writeln('    $line');
        }
      }
      sb.writeln();
    }

    sb.writeln('  # --- Layer 2: Core AI Behavior & Prompting ---');
    for (var synapse in synapses) {
      sb.writeln('  on synapse(${synapse.label}) it:');
      final payload = synapse.codePayload.trim();
      final code = payload.isNotEmpty ? payload : 'let summary = ask DigitalBrain.SDK.OpenAI.ChatGpt to "Analyze: {it.text}"';
      for (var line in code.split('\n')) {
        sb.writeln('    $line');
      }

      // Check outgoing connections from this synapse
      final outConns = activeConnections.where((c) => c.fromNodeId == synapse.id).toList();
      for (var conn in outConns) {
        final targetNode = activeNodes[conn.toNodeId];
        if (targetNode != null && targetNode.kind == NodeKind.signal) {
          sb.writeln('    emit signal(${targetNode.label})(summary: summary)');
        }
      }
      sb.writeln();
    }

    if (egressFilters.isNotEmpty) {
      sb.writeln('  # --- Layer 3: Outgoing Egress Interceptor ---');
      sb.writeln('  on egress:');
      for (var node in egressFilters) {
        final payload = node.codePayload.trim();
        final code = payload.isNotEmpty ? payload : 'log "Invoice processed: {it.summary}"';
        for (var line in code.split('\n')) {
          sb.writeln('    $line');
        }
      }
      sb.writeln();
    }

    return sb.toString().trimRight();
  }

  // Update a selected node's payload from code editor typing
  void handleCodeEditorSync(String text) {
    if (selectedNodeId == null) return;
    final selectedNode = _getActiveNode(selectedNodeId!);
    if (selectedNode == null) return;

    if (selectedNode.kind == NodeKind.synapse) {
      // Find the "on synapse(Name) it:" block in text
      final regExp = RegExp(
        r'on synapse\(' + RegExp.escape(selectedNode.label) + r'\) it:\n((?:\s+.*\n?)*)',
      );
      final match = regExp.firstMatch(text);
      if (match != null && match.groupCount >= 1) {
        final blockLines = match.group(1)!
            .split('\n')
            .map((line) => line.trim())
            .where((line) => line.isNotEmpty && !line.startsWith('emit signal'))
            .join('\n');
        
        if (blockLines != selectedNode.codePayload) {
          _setActiveNode(selectedNodeId!, selectedNode.copyWith(codePayload: blockLines));
          notifyListeners();
        }
      }
    } else if (selectedNode.kind == NodeKind.ingressFilter) {
      final regExp = RegExp(r'on ingress:\n((?:\s+.*\n?)*)');
      final match = regExp.firstMatch(text);
      if (match != null && match.groupCount >= 1) {
        final blockLines = match.group(1)!
            .split('\n')
            .map((line) => line.trim())
            .where((line) => line.isNotEmpty)
            .join('\n');
        
        if (blockLines != selectedNode.codePayload) {
          _setActiveNode(selectedNodeId!, selectedNode.copyWith(codePayload: blockLines));
          notifyListeners();
        }
      }
    } else if (selectedNode.kind == NodeKind.egressFilter) {
      final regExp = RegExp(r'on egress:\n((?:\s+.*\n?)*)');
      final match = regExp.firstMatch(text);
      if (match != null && match.groupCount >= 1) {
        final blockLines = match.group(1)!
            .split('\n')
            .map((line) => line.trim())
            .where((line) => line.isNotEmpty)
            .join('\n');
        
        if (blockLines != selectedNode.codePayload) {
          _setActiveNode(selectedNodeId!, selectedNode.copyWith(codePayload: blockLines));
          notifyListeners();
        }
      }
    }
  }

  void addConceptNode(String nodeId, String label, Offset position, String tone) {
    if (_nodes.containsKey(nodeId)) return;

    final newNode = VisualNode(
      id: nodeId,
      kind: NodeKind.synapse, // map it as a synapse node kind for visual wiring!
      label: label,
      position: position,
      ports: [
        NodePort(id: '${nodeId}_in', name: 'In', isInput: true, relativeOffset: const Offset(0, 50)),
        NodePort(id: '${nodeId}_out', name: 'Out', isInput: false, relativeOffset: const Offset(180, 50)),
      ],
      codePayload: 'Concept visual mapping projected on canvas',
    );

    _nodes[nodeId] = newNode;
    notifyListeners();
  }
}
