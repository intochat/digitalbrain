enum GraphNodeKind { hub, leaf, entity, module }

/// A world-space coordinate on the graph sphere.
final class GraphPoint {
  const GraphPoint(this.x, this.y, this.z);

  final double x;
  final double y;
  final double z;

  @override
  bool operator ==(Object other) =>
      other is GraphPoint && other.x == x && other.y == y && other.z == z;

  @override
  int get hashCode => Object.hash(x, y, z);

  @override
  String toString() => 'GraphPoint($x, $y, $z)';
}

final class GraphNode {
  const GraphNode({
    required this.id,
    required this.label,
    this.kind = GraphNodeKind.leaf,
    this.dimmed = false,
    this.cluster,
    this.position,
    this.iconKey,
  });

  final String id;
  final String label;
  final GraphNodeKind kind;
  final bool dimmed;

  /// Grouping key -- nodes sharing a cluster are coloured and placed together.
  final String? cluster;

  /// Explicit world coordinate. When null, `layoutGraph` derives a stable one.
  final GraphPoint? position;
  final String? iconKey;
}

final class GraphEdge {
  const GraphEdge({
    required this.id,
    required this.sourceId,
    required this.targetId,
    this.decorated = false,
    this.label,
  });

  final String id;
  final String sourceId;
  final String targetId;
  final bool decorated;
  final String? label;
}

final class GraphPulse {
  const GraphPulse({
    required this.fromId,
    required this.toId,
    required this.signature,
    this.operationId,
    this.outcome = GraphPulseOutcome.inFlight,
    this.at,
    this.updatedAt,
  });

  final String fromId;
  final String toId;
  final String signature;
  final String? operationId;
  final GraphPulseOutcome outcome;
  final DateTime? at;

  /// A later arrival or completion refreshes the fade, not the travel.
  final DateTime? updatedAt;

  bool get local => fromId == toId;

  /// Activity remains readable after its initial movement has finished.
  static const trailLifetime = Duration(seconds: 15);
  static const travelTime = Duration(milliseconds: 1800);

  double ageAt(DateTime now) => (updatedAt ?? at) == null
      ? 0
      : now.difference(updatedAt ?? at!).inMicroseconds /
            trailLifetime.inMicroseconds;

  double opacityAt(DateTime now) => (1 - ageAt(now)).clamp(0.0, 1.0);

  double travelAt(DateTime now) => at == null
      ? 0
      : (now.difference(at!).inMicroseconds / travelTime.inMicroseconds).clamp(
          0.0,
          1.0,
        );
}

enum GraphPulseOutcome { inFlight, arrived, completed, failed, signal }
