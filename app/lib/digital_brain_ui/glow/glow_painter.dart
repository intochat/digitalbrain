import 'dart:math';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';

import 'glow_icon_spec.dart';

/// Paper-heatmap technique: saveLayer → N semi-transparent dots with
/// MaskFilter.blur(BlurStyle.normal, σ) and BlendMode.plus → additive
/// accumulation builds a glowing silhouette; density drives brightness.
/// Source: flutterawesome.com/paper-heatmap-logo-a-flutter-demo-that-glows/
class GlowPainter extends CustomPainter {
  GlowPainter(this.spec, {this.dotCount = 36, this.blurSigma = 3.2});
  final GlowIconSpec spec;
  final int dotCount;
  final double blurSigma;

  @override
  void paint(Canvas canvas, Size size) {
    final rng = Random(spec.seed);
    final center = Offset(size.width / 2, size.height / 2);
    final radius = size.shortestSide / 2;
    final paint = Paint()
      ..color = spec.tone.withValues(alpha: 0.42)
      ..blendMode = BlendMode.plus
      ..maskFilter = MaskFilter.blur(ui.BlurStyle.normal, blurSigma);

    canvas.saveLayer(Offset.zero & size, Paint());
    for (var i = 0; i < dotCount; i++) {
      final p = _samplePoint(rng, center, radius, spec.shapeHint);
      final dotRadius = radius * 0.18 * (0.6 + rng.nextDouble() * 0.4);
      canvas.drawCircle(p, dotRadius, paint);
    }
    canvas.restore();
  }

  Offset _samplePoint(Random rng, Offset c, double r, String shapeHint) {
    switch (shapeHint) {
      case 'hex':
        final theta =
            (rng.nextInt(6) * pi / 3) + (rng.nextDouble() - 0.5) * 0.6;
        final rr = r * (0.3 + rng.nextDouble() * 0.6);
        return c + Offset(cos(theta) * rr, sin(theta) * rr);
      case 'leaf':
        final t = rng.nextDouble();
        final yOff = (t - 0.5) * r * 1.6;
        final xJit =
            (rng.nextDouble() - 0.5) *
            r *
            (1.0 - (2.0 * (t - 0.5)).abs()) *
            0.9;
        return c + Offset(xJit, yOff);
      case 'lens':
        final t = rng.nextDouble();
        final xOff = (t - 0.5) * r * 1.6;
        final yJit =
            (rng.nextDouble() - 0.5) *
            r *
            (1.0 - (2.0 * (t - 0.5)).abs()) *
            0.9;
        return c + Offset(xOff, yJit);
      case 'orb':
      default:
        final theta = rng.nextDouble() * 2 * pi;
        final rr = r * sqrt(rng.nextDouble()) * 0.85;
        return c + Offset(cos(theta) * rr, sin(theta) * rr);
    }
  }

  @override
  bool shouldRepaint(GlowPainter old) =>
      old.spec != spec ||
      old.dotCount != dotCount ||
      old.blurSigma != blurSigma;
}
