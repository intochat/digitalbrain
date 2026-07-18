import 'dart:math';

import 'package:flutter/material.dart';
import 'package:ino_flutter/state/timeline_bloc.dart';

class NeuralMap extends StatelessWidget {
  const NeuralMap({required this.snapshot, super.key});

  final StateSnapshot snapshot;

  @override
  Widget build(BuildContext context) {
    if (snapshot.activeNeurons.isEmpty) {
      return Center(
        child: Text(
          'No active neurons at this point',
          style: TextStyle(
            color: Theme.of(context).colorScheme.onSurface.withAlpha(120),
            fontSize: 14,
          ),
        ),
      );
    }

    return CustomPaint(
      painter: _NeuralMapPainter(
        neurons: snapshot.activeNeurons,
        nodeColor: Theme.of(context).colorScheme.primary,
        textBackground: Theme.of(context).colorScheme.surface,
      ),
      size: Size.infinite,
    );
  }
}

class _NeuralMapPainter extends CustomPainter {
  _NeuralMapPainter({
    required this.neurons,
    required this.nodeColor,
    required this.textBackground,
  });

  final List<String> neurons;
  final Color nodeColor;
  final Color textBackground;

  @override
  void paint(Canvas canvas, Size size) {
    final count = neurons.length;
    if (count == 0) return;

    final centerX = size.width / 2;
    final centerY = size.height / 2;
    final radius = min(centerX, centerY) * 0.65;
    const nodeRadius = 28.0;

    final positions = <Offset>[];
    for (var i = 0; i < count; i++) {
      final angle = 2 * pi * i / count - pi / 2;
      positions.add(Offset(
        centerX + radius * cos(angle),
        centerY + radius * sin(angle),
      ));
    }

    // connection lines between all pairs
    final linePaint = Paint()
      ..color = nodeColor.withAlpha(40)
      ..strokeWidth = 1.2;
    for (var i = 0; i < count; i++) {
      for (var j = i + 1; j < count; j++) {
        canvas.drawLine(positions[i], positions[j], linePaint);
      }
    }

    // glow + solid circle + label for each neuron
    final glowPaint = Paint()
      ..color = nodeColor.withAlpha(50)
      ..maskFilter = const MaskFilter.blur(BlurStyle.normal, 12);
    final solidPaint = Paint()..color = nodeColor;

    for (var i = 0; i < count; i++) {
      final pos = positions[i];

      canvas.drawCircle(pos, nodeRadius + 6, glowPaint);
      canvas.drawCircle(pos, nodeRadius, solidPaint);

      final label = neurons[i].length > 8
          ? neurons[i].substring(0, 8)
          : neurons[i];
      final textPainter = TextPainter(
        text: TextSpan(
          text: label,
          style: TextStyle(
            color: textBackground,
            fontSize: 10,
            fontWeight: FontWeight.w600,
          ),
        ),
        textDirection: TextDirection.ltr,
      )..layout(maxWidth: nodeRadius * 2 - 4);

      textPainter.paint(
        canvas,
        Offset(
          pos.dx - textPainter.width / 2,
          pos.dy - textPainter.height / 2,
        ),
      );
    }
  }

  @override
  bool shouldRepaint(covariant _NeuralMapPainter oldDelegate) {
    return neurons != oldDelegate.neurons ||
        nodeColor != oldDelegate.nodeColor;
  }
}
