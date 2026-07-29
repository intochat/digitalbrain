import 'dart:math' as math;

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

import 'topology_selection.dart';

final class GraphNode {
  const GraphNode({
    required this.id,
    required this.label,
    required this.module,
    required this.selection,
    required this.x,
    required this.y,
    required this.z,
  });

  final String id;
  final String label;
  final bool module;
  final BrainTopologySelection selection;
  final double x;
  final double y;
  final double z;
}

final class ProjectedNode {
  const ProjectedNode({
    required this.node,
    required this.center,
    required this.radius,
    required this.depth,
  });

  final GraphNode node;
  final Offset center;
  final double radius;
  final double depth;

  BrainTopologySelection get selection => node.selection;
}

bool hasPulseTarget(BrainTopologySnapshot topology, ChatTurnEvent? pulse) =>
    pulse != null &&
    topology.neurons.any((neuron) => neuron.id == pulse.neuronId);

bool hasTopologyNode(BrainTopologySnapshot topology, String id) =>
    topology.neurons.any((neuron) => neuron.id == id) ||
    topology.modules.any((module) => module.id == id);

List<ProjectedNode> projectTopology(
  BrainTopologySnapshot topology,
  Size size,
  double rotationX,
  double rotationY,
) {
  final graph = <GraphNode>[
    ...placeModules(topology.modules),
    ...placeNeurons(topology.neurons),
  ];
  final base = math.min(size.width, size.height) * 0.36;
  final center = Offset(size.width * 0.5, size.height * 0.51);
  final cosY = math.cos(rotationY);
  final sinY = math.sin(rotationY);
  final cosX = math.cos(rotationX);
  final sinX = math.sin(rotationX);

  final projected = <ProjectedNode>[];
  for (final node in graph) {
    final xY = node.x * cosY + node.z * sinY;
    final zY = -node.x * sinY + node.z * cosY;
    final yX = node.y * cosX - zY * sinX;
    final zX = node.y * sinX + zY * cosX;
    final perspective = 1.0 / (1.85 - zX * 0.36);
    final radius = (node.module ? 10.0 : 6.0) * (0.72 + perspective);

    projected.add(
      ProjectedNode(
        node: node,
        center: Offset(
          center.dx + xY * base * perspective,
          center.dy + yX * base * perspective,
        ),
        radius: radius,
        depth: zX,
      ),
    );
  }

  projected.sort((a, b) => a.depth.compareTo(b.depth));
  return projected;
}

Iterable<GraphNode> placeModules(List<BrainModule> modules) sync* {
  for (var index = 0; index < modules.length; index++) {
    final position = spherePosition(index, modules.length, 0.88, 0);
    final module = modules[index];
    yield GraphNode(
      id: module.id,
      label: brainModuleLabel(module),
      module: true,
      selection: BrainModuleSelection(module),
      x: position.x,
      y: position.y,
      z: position.z,
    );
  }
}

Iterable<GraphNode> placeNeurons(List<BrainNeuron> neurons) sync* {
  for (var index = 0; index < neurons.length; index++) {
    final position = spherePosition(index, neurons.length, 0.62, 1.3);
    final neuron = neurons[index];
    yield GraphNode(
      id: neuron.id,
      label: neuron.grainType,
      module: false,
      selection: BrainNeuronSelection(neuron),
      x: position.x,
      y: position.y,
      z: position.z,
    );
  }
}

({double x, double y, double z}) spherePosition(
  int index,
  int count,
  double radius,
  double phase,
) {
  if (count <= 1) {
    return (x: 0, y: 0, z: radius);
  }
  final y = 1 - (2 * (index + 0.5) / count);
  final ring = math.sqrt(math.max(0, 1 - y * y));
  final theta = index * math.pi * (3 - math.sqrt(5)) + phase;
  return (
    x: math.cos(theta) * ring * radius,
    y: y * radius,
    z: math.sin(theta) * ring * radius,
  );
}

ProjectedNode? hitTestTopology(List<ProjectedNode> nodes, Offset position) {
  for (final node in nodes.reversed) {
    if ((node.center - position).distance <= node.radius + 8) {
      return node;
    }
  }
  return null;
}
