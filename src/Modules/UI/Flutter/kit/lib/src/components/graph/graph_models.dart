enum GraphNodeKind { hub, leaf }

final class GraphNode {
  const GraphNode({
    required this.id,
    required this.label,
    this.kind = GraphNodeKind.leaf,
    this.dimmed = false,
  });

  final String id;
  final String label;
  final GraphNodeKind kind;
  final bool dimmed;
}

final class GraphEdge {
  const GraphEdge({
    required this.id,
    required this.sourceId,
    required this.targetId,
    this.decorated = false,
  });

  final String id;
  final String sourceId;
  final String targetId;
  final bool decorated;
}

final class GraphPulse {
  const GraphPulse({
    required this.fromId,
    required this.toId,
    required this.signature,
  });

  final String fromId;
  final String toId;
  final String signature;

  bool get local => fromId == toId;
}
