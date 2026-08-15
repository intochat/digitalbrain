import 'dart:async';
import 'dart:math' as math;

import 'package:flutter/material.dart';

import '../../models/kit_part.dart';
import '../../theme/kit_theme.dart';

/// A clock face for chat turns, dashboards, and windows.
///
/// With a [part] it counts down toward the timer's due instant; without one
/// it shows the current wall time.
final class KitClock extends StatefulWidget {
  const KitClock({super.key, this.part, this.showSeconds = true});

  final KitTimerPart? part;
  final bool showSeconds;

  @override
  State<KitClock> createState() => _KitClockState();
}

final class _KitClockState extends State<KitClock> {
  late DateTime _now;
  Timer? _timer;

  @override
  void initState() {
    super.initState();
    _now = DateTime.now();
    _timer = Timer.periodic(const Duration(seconds: 1), (_) {
      if (mounted) {
        setState(() => _now = DateTime.now());
      }
    });
  }

  @override
  void dispose() {
    _timer?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final part = widget.part;
    if (part == null) {
      return CustomPaint(
        key: const Key('kit_clock_face'),
        size: const Size.square(150),
        painter: _WallClockPainter(now: _now, showSeconds: widget.showSeconds),
      );
    }

    final remaining = part.dueAt.difference(_now.toUtc());
    final left = remaining.isNegative ? Duration.zero : remaining;
    final mm = left.inMinutes.remainder(60).toString().padLeft(2, '0');
    final ss = left.inSeconds.remainder(60).toString().padLeft(2, '0');
    final hours = left.inHours;
    final display = hours > 0 ? '$hours:$mm:$ss' : '$mm:$ss';

    return Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        Stack(
          alignment: Alignment.center,
          children: [
            CustomPaint(
              size: const Size.square(120),
              painter: _CountdownRingPainter(
                sweep: _sweepFor(left),
                done: left == Duration.zero,
              ),
            ),
            Text(
              display,
              key: const Key('kit_clock_remaining'),
              style: KitType.metaStrong.copyWith(fontSize: 20),
            ),
          ],
        ),
        const SizedBox(height: 8),
        Text(part.label, style: KitType.bodyMuted),
      ],
    );
  }

  // Without the original duration on the wire, sweep one full turn per hour
  // remaining so near timers visibly drain and far ones barely move.
  static double _sweepFor(Duration left) {
    if (left == Duration.zero) {
      return 0;
    }

    final withinHour = left.inMilliseconds.remainder(3600000) / 3600000;
    return withinHour == 0 ? 1.0 : withinHour;
  }
}

final class _CountdownRingPainter extends CustomPainter {
  const _CountdownRingPainter({required this.sweep, required this.done});

  final double sweep;
  final bool done;

  @override
  void paint(Canvas canvas, Size size) {
    final center = size.center(Offset.zero);
    final radius = size.shortestSide / 2 - 4;

    final track = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 6
      ..color = KitPalette.line;
    canvas.drawCircle(center, radius, track);

    if (sweep > 0) {
      final ring = Paint()
        ..style = PaintingStyle.stroke
        ..strokeWidth = 6
        ..strokeCap = StrokeCap.round
        ..color = KitPalette.signal;
      canvas.drawArc(
        Rect.fromCircle(center: center, radius: radius),
        -math.pi / 2,
        sweep * 2 * math.pi,
        false,
        ring,
      );
    }

    if (done) {
      final fill = Paint()..color = KitPalette.success.withValues(alpha: 0.2);
      canvas.drawCircle(center, radius, fill);
    }
  }

  @override
  bool shouldRepaint(_CountdownRingPainter oldDelegate) =>
      oldDelegate.sweep != sweep || oldDelegate.done != done;
}

final class _WallClockPainter extends CustomPainter {
  const _WallClockPainter({required this.now, required this.showSeconds});

  final DateTime now;
  final bool showSeconds;

  @override
  void paint(Canvas canvas, Size size) {
    final center = size.center(Offset.zero);
    final radius = size.shortestSide / 2 - 4;

    final face = Paint()..color = KitPalette.surfaceRaised;
    canvas.drawCircle(center, radius, face);

    final rim = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 2
      ..color = KitPalette.lineStrong;
    canvas.drawCircle(center, radius, rim);

    final tick = Paint()
      ..strokeWidth = 2
      ..color = KitPalette.textFaint;
    for (var hour = 0; hour < 12; hour++) {
      final angle = hour * math.pi / 6;
      final outer = center + Offset(math.sin(angle), -math.cos(angle)) * radius;
      final inner =
          center + Offset(math.sin(angle), -math.cos(angle)) * (radius - 8);
      canvas.drawLine(inner, outer, tick);
    }

    void hand(double turns, double length, double width, Color color) {
      final angle = turns * 2 * math.pi;
      final paint = Paint()
        ..strokeWidth = width
        ..strokeCap = StrokeCap.round
        ..color = color;
      canvas.drawLine(
        center,
        center + Offset(math.sin(angle), -math.cos(angle)) * length,
        paint,
      );
    }

    hand(
      (now.hour % 12 + now.minute / 60) / 12,
      radius * 0.5,
      4,
      KitPalette.textPrimary,
    );
    hand(now.minute / 60, radius * 0.72, 3, KitPalette.textPrimary);
    if (showSeconds) {
      hand(now.second / 60, radius * 0.8, 1.5, KitPalette.signal);
    }

    canvas.drawCircle(center, 3, Paint()..color = KitPalette.signal);
  }

  @override
  bool shouldRepaint(_WallClockPainter oldDelegate) =>
      oldDelegate.now != now || oldDelegate.showSeconds != showSeconds;
}
