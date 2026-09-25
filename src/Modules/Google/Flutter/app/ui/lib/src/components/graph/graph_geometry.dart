import 'dart:math' as math;

import 'package:flutter/widgets.dart';

import 'graph_models.dart';
import 'spatial_layout.dart';

final class ProjectedGraphNode {
  const ProjectedGraphNode({
    required this.node,
    required this.center,
    required this.radius,
    required this.depth,
  });

  final GraphNode node;
  final Offset center;
  final double radius;
  final double depth;
}

final class ProjectedGraphEdge {
  const ProjectedGraphEdge({
    required this.edge,
    required this.from,
    required this.to,
  });

  final GraphEdge edge;
  final ProjectedGraphNode from;
  final ProjectedGraphNode to;

  double get depth => math.min(from.depth, to.depth);
}

List<ProjectedGraphNode> projectGraphNodes(
  List<GraphNode> nodes,
  Size size,
  double rotationX,
  double rotationY,
) {
  final placed = spatialLayoutGraph(nodes);
  final center = Offset(size.width * 0.5, size.height * 0.51);
  final cosY = math.cos(rotationY);
  final sinY = math.sin(rotationY);
  final cosX = math.cos(rotationX);
  final sinX = math.sin(rotationX);

  final rotated = <(GraphNode, double, double, double, double)>[];
  for (final node in nodes) {
    final point = placed[node.id]!;
    final xY = point.x * cosY + point.z * sinY;
    final zY = -point.x * sinY + point.z * cosY;
    final yX = point.y * cosX - zY * sinX;
    final zX = point.y * sinX + zY * cosX;
    final perspective = 1.0 / (1.4 - zX * 0.08);
    rotated.add((node, xY * perspective, yX * perspective, zX, perspective));
  }

  if (rotated.isEmpty) return const [];
  final minX = rotated.map((item) => item.$2).reduce(math.min);
  final maxX = rotated.map((item) => item.$2).reduce(math.max);
  final minY = rotated.map((item) => item.$3).reduce(math.min);
  final maxY = rotated.map((item) => item.$3).reduce(math.max);
  final scaleX = size.width * 0.72 / math.max(maxX - minX, 1);
  final scaleY = size.height * 0.70 / math.max(maxY - minY, 1);
  final originX = (minX + maxX) / 2;
  final originY = (minY + maxY) / 2;

  final projected = <ProjectedGraphNode>[];
  for (final (node, x, y, depth, perspective) in rotated) {
    final radius =
        (node.kind == GraphNodeKind.hub ? 10.0 : 6.0) * (0.72 + perspective);

    projected.add(
      ProjectedGraphNode(
        node: node,
        center: Offset(
          center.dx + (x - originX) * scaleX,
          center.dy + (y - originY) * scaleY,
        ),
        radius: radius,
        depth: depth,
      ),
    );
  }

  projected.sort((a, b) => a.depth.compareTo(b.depth));
  return projected;
}

List<ProjectedGraphEdge> projectGraphEdges(
  List<GraphEdge> edges,
  List<ProjectedGraphNode> nodes,
) {
  final byId = {for (final node in nodes) node.node.id: node};
  final projected = <ProjectedGraphEdge>[];
  for (final edge in edges) {
    final from = byId[edge.sourceId];
    final to = byId[edge.targetId];
    if (from == null || to == null || identical(from, to)) {
      continue;
    }
    projected.add(ProjectedGraphEdge(edge: edge, from: from, to: to));
  }
  projected.sort((a, b) => a.depth.compareTo(b.depth));
  return projected;
}

Offset graphEdgeControl(ProjectedGraphEdge edge, Offset canvasCenter) {
  final mid = Offset(
    (edge.from.center.dx + edge.to.center.dx) / 2,
    (edge.from.center.dy + edge.to.center.dy) / 2,
  );
  return Offset.lerp(mid, canvasCenter, 0.18)! - const Offset(0, 14);
}

Offset graphQuadraticPoint(Offset a, Offset control, Offset b, double t) {
  final u = 1 - t;
  return a * (u * u) + control * (2 * u * t) + b * (t * t);
}

ProjectedGraphNode? hitTestGraphNodes(
  List<ProjectedGraphNode> nodes,
  Offset position,
) {
  for (final node in nodes.reversed) {
    if ((node.center - position).distance <= node.radius + 8) {
      return node;
    }
  }
  return null;
}

ProjectedGraphEdge? hitTestGraphEdges(
  List<ProjectedGraphEdge> edges,
  Offset position,
  Offset canvasCenter,
) {
  for (final edge in edges.reversed) {
    final control = graphEdgeControl(edge, canvasCenter);
    for (var step = 1; step < 10; step++) {
      final sample = graphQuadraticPoint(
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
