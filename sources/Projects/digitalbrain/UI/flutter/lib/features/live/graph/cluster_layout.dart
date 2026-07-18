import 'dart:math';

import 'package:vector_math/vector_math_64.dart' as vm;

import 'domain_palette.dart';

class GraphNode {
  GraphNode({
    required this.id,
    required this.domain,
    required this.position,
    required this.firstSeenAt,
    required this.lastSeenAt,
    this.isActive = false,
  });
  final String id;
  String domain;
  final vm.Vector3 position;
  vm.Vector3 velocity = vm.Vector3.zero();
  vm.Vector3 force = vm.Vector3.zero();
  DateTime firstSeenAt;
  DateTime lastSeenAt;
  bool isActive;
}

class GraphEdge {
  GraphEdge({
    required this.at,
    required this.fromId,
    required this.toId,
    required this.typeName,
    required this.methodName,
    required this.correlationId,
    required this.payload,
    required this.pulseUntil,
  });
  final DateTime at;
  final String fromId;
  final String toId;
  final String typeName;
  final String methodName;
  final String correlationId;
  final List<int> payload;
  final DateTime pulseUntil;
}

vm.Vector3 sphericalSeed(String id, String domain) {
  final base = styleForDomain(domain).anchor;
  final rng = Random(id.hashCode);
  // Scatter within a small ball around the domain anchor so neurons in the
  // same domain start near each other but don't overlap.
  final dx = (rng.nextDouble() - 0.5) * 80;
  final dy = (rng.nextDouble() - 0.5) * 80;
  final dz = (rng.nextDouble() - 0.5) * 80;
  return base + vm.Vector3(dx, dy, dz);
}

void stepLayout({
  required List<GraphNode> nodes,
  required List<GraphEdge> edges,
  required double dt,
  double repulsion = 4500.0,
  double attraction = 0.04,
  double cohesion = 0.02,
  double damping = 0.85,
  double desiredEdgeLen = 140.0,
}) {
  if (nodes.isEmpty) return;

  for (final n in nodes) {
    n.force.setZero();
  }

  // O(n^2) repulsion — clamped to 200 nodes is fine. Past that the inner
  // loop turns into a perf cliff; switch to a quadtree if it ever matters.
  for (var i = 0; i < nodes.length; i++) {
    for (var j = i + 1; j < nodes.length; j++) {
      final delta = nodes[i].position - nodes[j].position;
      final d2 = delta.length2.clamp(1.0, 1e9);
      final d = sqrt(d2);
      final f = repulsion / d2;
      final dir = delta / d;
      nodes[i].force.add(dir * f);
      nodes[j].force.sub(dir * f);
    }
  }

  // Edge attraction (existing semantics from brain_view_screen.dart).
  final byId = {for (final n in nodes) n.id: n};
  for (final e in edges) {
    final a = byId[e.fromId];
    final b = byId[e.toId];
    if (a == null || b == null) continue;
    final delta = b.position - a.position;
    final d = delta.length;
    if (d < 0.01) continue;
    final f = (d - desiredEdgeLen) * attraction;
    final dir = delta / d;
    a.force.add(dir * f);
    b.force.sub(dir * f);
  }

  // Cluster cohesion: pull each node toward its domain anchor.
  for (final n in nodes) {
    final anchor = styleForDomain(n.domain).anchor;
    final delta = anchor - n.position;
    n.force.add(delta * cohesion);
  }

  for (final n in nodes) {
    n.velocity = (n.velocity + n.force * dt) * damping;
    n.position.add(n.velocity * dt);
  }
}
