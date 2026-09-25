import 'dart:math' as math;

import 'package:flutter/material.dart';

import '../../theme/ui_theme.dart';
import 'graph_geometry.dart';
import 'graph_models.dart';

final class GraphPainter extends CustomPainter {
  const GraphPainter({
    required this.nodes,
    required this.edges,
    required this.pulse,
    this.pulses = const [],
    this.traceEdges = const [],
    this.now,
    this.reducedMotion = false,
    required this.pulseValue,
    this.highlightEdgeId,
  });

  final List<ProjectedGraphNode> nodes;
  final List<ProjectedGraphEdge> edges;
  final GraphPulse? pulse;
  final List<GraphPulse> pulses;
  final List<ProjectedGraphEdge> traceEdges;
  final DateTime? now;
  final bool reducedMotion;
  final double pulseValue;
  final String? highlightEdgeId;

  @override
  void paint(Canvas canvas, Size size) {
    final center = Offset(size.width * 0.5, size.height * 0.51);
    _paintEdges(canvas, center);
    _paintTracePath(canvas, center);
    _paintPulse(canvas, center, size);
    _paintNodes(canvas);
  }

  void _paintEdges(Canvas canvas, Offset center) {
    for (final edge in edges) {
      final control = graphEdgeControl(edge, center);
      final depthAlpha = (0.45 + (edge.depth + 1) * 0.2).clamp(0.3, 0.9);
      final recent = edge.edge.id == highlightEdgeId;
      final color = recent ? UiPalette.signal : UiPalette.owner;
      final alpha = recent ? 0.95 : math.max(depthAlpha, 0.68);

      final path = Path()
        ..moveTo(edge.from.center.dx, edge.from.center.dy)
        ..quadraticBezierTo(
          control.dx,
          control.dy,
          edge.to.center.dx,
          edge.to.center.dy,
        );
      canvas.drawPath(
        path,
        Paint()
          ..style = PaintingStyle.stroke
          ..strokeWidth = recent ? 2.6 : 1.8
          ..color = color.withValues(alpha: alpha),
      );

      final tip = graphQuadraticPoint(
        edge.from.center,
        control,
        edge.to.center,
        0.86,
      );
      final tail = graphQuadraticPoint(
        edge.from.center,
        control,
        edge.to.center,
        0.78,
      );
      final direction = (tip - tail).direction;
      const arrow = 6.0;
      final head = Path()
        ..moveTo(tip.dx, tip.dy)
        ..lineTo(
          tip.dx - arrow * math.cos(direction - 0.45),
          tip.dy - arrow * math.sin(direction - 0.45),
        )
        ..lineTo(
          tip.dx - arrow * math.cos(direction + 0.45),
          tip.dy - arrow * math.sin(direction + 0.45),
        )
        ..close();
      canvas.drawPath(head, Paint()..color = color.withValues(alpha: alpha));

      if (edge.edge.decorated) {
        final bead = graphQuadraticPoint(
          edge.from.center,
          control,
          edge.to.center,
          0.5,
        );
        canvas.drawCircle(
          bead,
          3.2,
          Paint()..color = UiPalette.owner.withValues(alpha: depthAlpha),
        );
        canvas.drawCircle(
          bead,
          3.2,
          Paint()
            ..style = PaintingStyle.stroke
            ..strokeWidth = 1
            ..color = UiPalette.textPrimary.withValues(alpha: 0.4),
        );
      }
    }
  }

  void _paintTracePath(Canvas canvas, Offset center) {
    for (final edge in traceEdges) {
      final control = graphEdgeControl(edge, center);
      final path = Path()
        ..moveTo(edge.from.center.dx, edge.from.center.dy)
        ..quadraticBezierTo(
          control.dx,
          control.dy,
          edge.to.center.dx,
          edge.to.center.dy,
        );
      canvas.drawPath(
        path,
        Paint()
          ..style = PaintingStyle.stroke
          ..strokeWidth = 3
          ..color = UiPalette.signal.withValues(alpha: 0.95),
      );
      final label = edge.edge.label;
      if (label == null) continue;
      final mid = graphQuadraticPoint(
        edge.from.center,
        control,
        edge.to.center,
        0.5,
      );
      final text = TextPainter(
        text: TextSpan(
          text: label,
          style: UiType.metaStrong.copyWith(color: UiPalette.surface),
        ),
        textDirection: TextDirection.ltr,
      )..layout();
      canvas.drawCircle(
        mid,
        math.max(text.width, text.height) / 2 + 5,
        Paint()..color = UiPalette.signal,
      );
      text.paint(canvas, mid - Offset(text.width / 2, text.height / 2));
    }
  }

  void _paintPulse(Canvas canvas, Offset center, Size size) {
    for (final active in pulses.isEmpty ? [?pulse] : pulses) {
      _paintOnePulse(canvas, center, size, active);
    }
  }

  void _paintOnePulse(
    Canvas canvas,
    Offset center,
    Size size,
    GraphPulse active,
  ) {
    final clock = now ?? DateTime.now().toUtc();
    final age = active.ageAt(clock);
    if (age < 0 || age >= 1) return;
    final opacity = active.opacityAt(clock);
    final progress = reducedMotion
        ? 1.0
        : active.at == null
        ? pulseValue
        : active.travelAt(clock);

    final target = nodes
        .where((node) => node.node.id == active.toId)
        .firstOrNull;
    if (target == null) {
      return;
    }
    final source = nodes
        .where((node) => node.node.id == active.fromId)
        .firstOrNull;

    final radius = math.min(size.width, size.height) * 0.34;
    final wave = math.sin(progress * math.pi).abs();
    final color = switch (active.outcome) {
      GraphPulseOutcome.completed => UiPalette.success,
      GraphPulseOutcome.failed => Colors.redAccent,
      GraphPulseOutcome.arrived => Colors.lightBlueAccent,
      _ => UiPalette.signal,
    };
    if (source != null && source.node.id != target.node.id) {
      final path = Path()
        ..moveTo(source.center.dx, source.center.dy)
        ..quadraticBezierTo(
          center.dx,
          center.dy - radius * 0.45,
          target.center.dx,
          target.center.dy,
        );
      canvas.drawPath(
        path,
        Paint()
          ..style = PaintingStyle.stroke
          ..strokeWidth = 2 + opacity * 3.5
          ..color = color.withValues(alpha: 0.12 + opacity * 0.83),
      );
      if (progress < 1 && !reducedMotion) {
        final metric = path.computeMetrics().first;
        final point = metric
            .getTangentForOffset(metric.length * progress)
            ?.position;
        if (point != null) {
          final tangent = metric.getTangentForOffset(
            (metric.length * progress - 22).clamp(0.0, metric.length),
          );
          if (tangent != null) {
            canvas.drawLine(
              tangent.position,
              point,
              Paint()
                ..color = color.withValues(alpha: opacity)
                ..strokeWidth = 5
                ..strokeCap = StrokeCap.round,
            );
          }
        }
      }
    }
    final reaction = active.local || progress >= 0.85;
    canvas.drawCircle(
      reaction ? target.center : source?.center ?? target.center,
      (reaction ? target.radius : source?.radius ?? target.radius) +
          8 +
          wave * 16,
      Paint()
        ..style = PaintingStyle.stroke
        ..strokeWidth = 2.5
        ..color = color.withValues(alpha: opacity * (0.85 - wave * 0.3)),
    );
  }

  void _paintNodes(Canvas canvas) {
    for (final projected in nodes) {
      final node = projected.node;
      final color = switch (node.kind) {
        GraphNodeKind.hub => UiPalette.signal,
        GraphNodeKind.entity => UiPalette.success,
        GraphNodeKind.module => UiPalette.signal,
        GraphNodeKind.leaf => UiPalette.owner,
      };
      final dimFactor = node.dimmed ? 0.45 : 1.0;
      final depthAlpha =
          ((0.5 + (projected.depth + 1) * 0.22).clamp(0.35, 1.0)) * dimFactor;
      canvas.drawCircle(
        projected.center,
        projected.radius * 1.8,
        Paint()
          ..color = color.withValues(alpha: 0.06 * depthAlpha)
          ..maskFilter = const MaskFilter.blur(BlurStyle.normal, 8),
      );
      _paintNodeBody(canvas, projected, color, depthAlpha);

      final text = TextPainter(
        text: TextSpan(
          text: node.label,
          style: UiType.meta.copyWith(
            color: UiPalette.textPrimary.withValues(alpha: depthAlpha),
          ),
        ),
        textDirection: TextDirection.ltr,
      )..layout(maxWidth: 120);
      text.paint(
        canvas,
        projected.center + Offset(-text.width / 2, projected.radius + 7),
      );
    }
  }

  static void _paintNodeBody(
    Canvas canvas,
    ProjectedGraphNode projected,
    Color color,
    double depthAlpha,
  ) {
    final fill = Paint()..color = color.withValues(alpha: depthAlpha);
    final stroke = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1
      ..color = UiPalette.textPrimary.withValues(alpha: 0.3);
    final r = projected.radius;
    final c = projected.center;

    switch (projected.node.kind) {
      case GraphNodeKind.entity:
        final rect = Rect.fromCenter(center: c, width: r * 2, height: r * 2);
        canvas.drawRRect(RRect.fromRectXY(rect, 3, 3), fill);
        canvas.drawRRect(RRect.fromRectXY(rect, 3, 3), stroke);
      case GraphNodeKind.module:
        final diamond = Path()
          ..moveTo(c.dx, c.dy - r)
          ..lineTo(c.dx + r, c.dy)
          ..lineTo(c.dx, c.dy + r)
          ..lineTo(c.dx - r, c.dy)
          ..close();
        canvas.drawPath(diamond, fill);
        canvas.drawPath(diamond, stroke);
      case GraphNodeKind.hub:
      case GraphNodeKind.leaf:
        canvas.drawCircle(c, r, fill);
        canvas.drawCircle(c, r, stroke);
    }
  }

  @override
  bool shouldRepaint(covariant GraphPainter oldDelegate) =>
      oldDelegate.nodes != nodes ||
      oldDelegate.edges != edges ||
      oldDelegate.pulse != pulse ||
      oldDelegate.pulses != pulses ||
      oldDelegate.traceEdges != traceEdges ||
      oldDelegate.now != now ||
      oldDelegate.reducedMotion != reducedMotion ||
      oldDelegate.pulseValue != pulseValue ||
      oldDelegate.highlightEdgeId != highlightEdgeId;
}
