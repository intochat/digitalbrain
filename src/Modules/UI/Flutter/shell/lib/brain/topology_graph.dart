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

bool hasPulseTarget(ChatTurnEvent? pulse) => pulse != null;

List<BrainNeuron> displayedNeurons(
  BrainTopologySnapshot topology,
  ChatTurnEvent? pulse,
) {
  final neurons = List<BrainNeuron>.of(topology.neurons);
  void ensure(String id) {
    if (id.isEmpty || neurons.any((neuron) => neuron.id == id)) {
      return;
    }
    neurons.add(optimisticNeuron(id));
  }

  if (pulse != null) {
    ensure(pulse.neuronId);
    ensure(pulse.caller);
  }

  for (final connection in topology.connections) {
    ensure(connection.source);
    ensure(connection.target);
  }

  return neurons;
}

BrainNeuron optimisticNeuron(String id) {
  final separator = id.indexOf(':');
  if (separator <= 0 || separator == id.length - 1) {
    return BrainNeuron(
      id: id,
      grainType: id,
      identity: id,
      placement: 'pending',
    );
  }

  return BrainNeuron(
    id: id,
    grainType: id.substring(0, separator),
    identity: id.substring(separator + 1),
    placement: 'pending',
  );
}

List<ProjectedNode> projectTopology(
  BrainTopologySnapshot topology,
  Size size,
  double rotationX,
  double rotationY, {
  ChatTurnEvent? pulse,
}) {
  final graph = <GraphNode>[
    ...placeModules(topology.modules),
    ...placeNeurons(displayedNeurons(topology, pulse)),
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

final class ProjectedEdge {
  const ProjectedEdge({
    required this.connection,
    required this.from,
    required this.to,
  });

  final BrainConnection connection;
  final ProjectedNode from;
  final ProjectedNode to;

  double get depth => math.min(from.depth, to.depth);
}

List<ProjectedEdge> projectConnections(
  BrainTopologySnapshot topology,
  List<ProjectedNode> nodes,
) {
  final byId = {for (final node in nodes) node.node.id: node};
  final edges = <ProjectedEdge>[];
  for (final connection in topology.connections) {
    final from = byId[connection.source];
    final to = byId[connection.target];
    if (from == null || to == null || identical(from, to)) {
      continue;
    }
    edges.add(ProjectedEdge(connection: connection, from: from, to: to));
  }
  edges.sort((a, b) => a.depth.compareTo(b.depth));
  return edges;
}

Offset connectionControl(ProjectedEdge edge, Offset canvasCenter) {
  final mid = Offset(
    (edge.from.center.dx + edge.to.center.dx) / 2,
    (edge.from.center.dy + edge.to.center.dy) / 2,
  );
  return Offset.lerp(mid, canvasCenter, 0.18)! - const Offset(0, 14);
}

Offset quadraticPoint(Offset a, Offset control, Offset b, double t) {
  final u = 1 - t;
  return a * (u * u) + control * (2 * u * t) + b * (t * t);
}

ProjectedEdge? hitTestConnections(
  List<ProjectedEdge> edges,
  Offset position,
  Offset canvasCenter,
) {
  for (final edge in edges.reversed) {
    final control = connectionControl(edge, canvasCenter);
    for (var step = 1; step < 10; step++) {
      final sample = quadraticPoint(
        edge.from.center,
        control,
        edge.to.center,
        step / 10,
      );
      if ((sample - position).distance <= 9) {
        return edge;
      }
    }
  }
  return null;
}
