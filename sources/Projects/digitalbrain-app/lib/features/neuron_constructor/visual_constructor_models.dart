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
  final String? referencedNeuronFqn; // External neuron reference FQN, if any

  VisualNode({
    required this.id,
    required this.kind,
    required this.label,
    required this.position,
    this.size = const Size(180, 100),
    required this.ports,
    this.codePayload = '',
    this.referencedNeuronFqn,
  });

  VisualNode copyWith({
    Offset? position,
    String? label,
    String? codePayload,
    String? referencedNeuronFqn,
  }) {
    return VisualNode(
      id: id,
      kind: kind,
      label: label ?? this.label,
      position: position ?? this.position,
      size: size,
      ports: ports,
      codePayload: codePayload ?? this.codePayload,
      referencedNeuronFqn: referencedNeuronFqn ?? this.referencedNeuronFqn,
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
