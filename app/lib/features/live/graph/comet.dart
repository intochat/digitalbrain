import 'package:digitalbrain_flutter/digital_brain_ui/digital_brain_ui.dart';
import 'package:flutter/material.dart';
import 'package:vector_math/vector_math_64.dart' as vm;

import 'brain_painter.dart';
import 'cluster_layout.dart';
import 'domain_palette.dart';

class Comet {
  Comet({
    required this.edge,
    required this.startedAt,
    this.travelDuration = const Duration(milliseconds: 900),
    this.tailLinger = const Duration(seconds: 6),
  });
  final GraphEdge edge;
  final DateTime startedAt;
  final Duration travelDuration;
  final Duration tailLinger;
  DateTime? shatterAt;

  void shatter() {
    shatterAt ??= DateTime.now();
  }

  double shatterFactor(DateTime now) {
    if (shatterAt == null) return 1.0;
    final elapsedMs = now.difference(shatterAt!).inMilliseconds.toDouble();
    return (1.0 - elapsedMs / 200.0).clamp(0.0, 1.0);
  }

  bool isExpired(DateTime now) {
    if (shatterAt != null &&
        now.difference(shatterAt!) >= const Duration(milliseconds: 200)) {
      return true;
    }
    return now.difference(startedAt) > travelDuration + tailLinger;
  }

  // 0..1 along the bezier arc (clamped after the head reaches the end).
  double headProgress(DateTime now) {
    final elapsedMs = now.difference(startedAt).inMilliseconds;
    return (elapsedMs / travelDuration.inMilliseconds).clamp(0.0, 1.0);
  }

  // Tail opacity decays from 1.0 to ~0.1 over tailLinger.
  double tailOpacity(DateTime now) {
    final t = now.difference(startedAt);
    if (t < travelDuration) return 1.0;
    final lingerMs = (t - travelDuration).inMilliseconds.toDouble();
    final maxMs = tailLinger.inMilliseconds.toDouble();
    return (1.0 - lingerMs / maxMs).clamp(0.10, 1.0) * 0.7;
  }
}

void drawComets({
  required Canvas canvas,
  required List<Comet> comets,
  required Map<String, GraphNode> nodesById,
  required Projection projection,
  required DateTime now,
}) {
  for (final comet in comets) {
    final fromNode = nodesById[comet.edge.fromId];
    final toNode = nodesById[comet.edge.toId];
    if (fromNode == null || toNode == null) continue;

    final start = projection.project(fromNode.position);
    final end = projection.project(toNode.position);
    final midPoint = _bezierMidpoint(fromNode.position, toNode.position);
    final mid = projection.project(midPoint);

    final color = colorForSynapseType(comet.edge.typeName);
    final t = comet.headProgress(now);
    final sf = comet.shatterFactor(now);
    final tailAlpha = comet.tailOpacity(now) * sf;

    final path = Path()..moveTo(start.screen.dx, start.screen.dy);
    path.quadraticBezierTo(
      mid.screen.dx,
      mid.screen.dy,
      end.screen.dx,
      end.screen.dy,
    );

    // Soft blurred glow under the tail so the synapse arc is unmistakable.
    final glowPaint = Paint()
      ..color = color.withValues(alpha: tailAlpha * 0.55)
      ..strokeWidth = 5.0
      ..style = PaintingStyle.stroke
      ..strokeCap = StrokeCap.round
      ..maskFilter = const MaskFilter.blur(BlurStyle.normal, 4.0);
    canvas.drawPath(path, glowPaint);

    // Bright tail core.
    final tailPaint = Paint()
      ..color = color.withValues(alpha: tailAlpha)
      ..strokeWidth = 2.2
      ..style = PaintingStyle.stroke
      ..strokeCap = StrokeCap.round;
    canvas.drawPath(path, tailPaint);

    // Head: only while in flight (collapses to head position on shatter).
    if (t < 1.0) {
      final head = _samplePointOnQuadratic(
        start.screen,
        mid.screen,
        end.screen,
        t,
      );
      _drawCometHead(canvas, head, color, sf, comet.edge.typeName);
    }
  }
}

const double _kCometHeadSize = 24;

void _drawCometHead(
  Canvas canvas,
  Offset head,
  Color color,
  double shatterFactor,
  String typeName,
) {
  final headSpec = GlowIconSpec(
    seed: typeName.hashCode,
    size: _kCometHeadSize,
    tone: color,
    shapeHint: 'orb',
  );
  canvas.save();
  canvas.translate(
    head.dx - _kCometHeadSize / 2,
    head.dy - _kCometHeadSize / 2,
  );
  GlowPainter(
    headSpec,
  ).paint(canvas, const Size(_kCometHeadSize, _kCometHeadSize));
  canvas.restore();

  // Crisp white nucleus on top so the head reads against bright filaments,
  // and so shattered comets still fade via shatterFactor.
  canvas.drawCircle(
    head,
    2.6,
    Paint()..color = Colors.white.withValues(alpha: 0.95 * shatterFactor),
  );
}

vm.Vector3 _bezierMidpoint(vm.Vector3 a, vm.Vector3 b) {
  // Push the midpoint outward from the origin so the arc visibly bulges
  // across the sphere's surface rather than slicing through the centre.
  final m = (a + b) / 2;
  final n = m.length < 1e-3 ? vm.Vector3(0, 0, 1) : m.normalized();
  return m + n * 60.0;
}

Offset _samplePointOnQuadratic(Offset p0, Offset p1, Offset p2, double t) {
  final u = 1 - t;
  return Offset(
    u * u * p0.dx + 2 * u * t * p1.dx + t * t * p2.dx,
    u * u * p0.dy + 2 * u * t * p1.dy + t * t * p2.dy,
  );
}
