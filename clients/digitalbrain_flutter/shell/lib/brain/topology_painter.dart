import 'dart:math' as math;

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

import '../brain_theme.dart';
import 'topology_graph.dart';

final class TopologyPainter extends CustomPainter {
  const TopologyPainter({
    required this.nodes,
    required this.pulse,
    required this.pulseValue,
  });

  final List<ProjectedNode> nodes;
  final ChatTurnEvent? pulse;
  final double pulseValue;

  @override
  void paint(Canvas canvas, Size size) {
    final center = Offset(size.width * 0.5, size.height * 0.51);
    final hull = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1
      ..color = BrainPalette.lineStrong.withValues(alpha: 0.55);
    final radius = math.min(size.width, size.height) * 0.34;
    canvas.drawOval(
      Rect.fromCenter(
        center: center,
        width: radius * 2.15,
        height: radius * 1.55,
      ),
      hull,
    );
    canvas.drawOval(
      Rect.fromCenter(
        center: center,
        width: radius * 1.35,
        height: radius * 2.05,
      ),
      hull..color = BrainPalette.line.withValues(alpha: 0.5),
    );

    final pulseTarget = pulse == null
        ? null
        : nodes.where((node) => node.node.id == pulse!.neuronId).firstOrNull;
    final pulseSource = pulse == null
        ? null
        : nodes.where((node) => node.node.id == pulse!.caller).firstOrNull;
    if (pulseTarget != null) {
      final wave = math.sin(pulseValue * math.pi).abs();
      if (pulseSource != null && pulseSource.node.id != pulseTarget.node.id) {
        final path = Path()
          ..moveTo(pulseSource.center.dx, pulseSource.center.dy)
          ..quadraticBezierTo(
            center.dx,
            center.dy - radius * 0.45,
            pulseTarget.center.dx,
            pulseTarget.center.dy,
          );
        canvas.drawPath(
          path,
          Paint()
            ..style = PaintingStyle.stroke
            ..strokeWidth = 2.4
            ..color = BrainPalette.signal.withValues(alpha: 0.25 + wave * 0.7),
        );
      }
      canvas.drawCircle(
        pulseTarget.center,
        pulseTarget.radius + 8 + wave * 16,
        Paint()
          ..style = PaintingStyle.stroke
          ..strokeWidth = 2
          ..color = BrainPalette.signal.withValues(alpha: 0.7 - wave * 0.3),
      );
    }

    for (final projected in nodes) {
      final node = projected.node;
      final color = node.module ? BrainPalette.signal : BrainPalette.owner;
      final depthAlpha = (0.5 + (projected.depth + 1) * 0.22).clamp(0.35, 1.0);
      canvas.drawCircle(
        projected.center,
        projected.radius * 1.8,
        Paint()
          ..color = color.withValues(alpha: 0.06 * depthAlpha)
          ..maskFilter = const MaskFilter.blur(BlurStyle.normal, 8),
      );
      canvas.drawCircle(
        projected.center,
        projected.radius,
        Paint()..color = color.withValues(alpha: depthAlpha),
      );
      canvas.drawCircle(
        projected.center,
        projected.radius,
        Paint()
          ..style = PaintingStyle.stroke
          ..strokeWidth = 1
          ..color = BrainPalette.textPrimary.withValues(alpha: 0.3),
      );

      if (node.module || node.id == pulse?.neuronId) {
        final text = TextPainter(
          text: TextSpan(
            text: node.label,
            style: BrainType.meta.copyWith(
              color: BrainPalette.textPrimary.withValues(alpha: depthAlpha),
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
  }

  @override
  bool shouldRepaint(covariant TopologyPainter oldDelegate) =>
      oldDelegate.nodes != nodes ||
      oldDelegate.pulse != pulse ||
      oldDelegate.pulseValue != pulseValue;
}
