import 'dart:async';
import 'dart:math' as math;
import 'package:flutter/material.dart';
import '../brain_theme.dart';

final class KitAnalogClock extends StatefulWidget {
  const KitAnalogClock({super.key, this.showSeconds = true});

  final bool showSeconds;

  @override
  State<KitAnalogClock> createState() => _KitAnalogClockState();
}

final class _KitAnalogClockState extends State<KitAnalogClock> {
  late DateTime _now;
  Timer? _timer;

  @override
  void initState() {
    super.initState();
    _now = DateTime.now();
    _timer = Timer.periodic(const Duration(seconds: 1), (_) {
      if (mounted) setState(() => _now = DateTime.now());
    });
  }

  @override
  void dispose() {
    _timer?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return CustomPaint(
      painter: _AnalogClockPainter(now: _now, showSeconds: widget.showSeconds),
    );
  }
}

final class KitCountdownClock extends StatefulWidget {
  const KitCountdownClock({super.key, required this.duration});

  final Duration duration;

  @override
  State<KitCountdownClock> createState() => _KitCountdownClockState();
}

final class _KitCountdownClockState extends State<KitCountdownClock> {
  late final DateTime _endsAt;
  late Duration _remaining;
  Timer? _timer;

  @override
  void initState() {
    super.initState();
    _endsAt = DateTime.now().add(widget.duration);
    _remaining = widget.duration;
    _timer = Timer.periodic(const Duration(milliseconds: 200), (_) {
      if (!mounted) return;
      final left = _endsAt.difference(DateTime.now());
      setState(() => _remaining = left.isNegative ? Duration.zero : left);
    });
  }

  @override
  void dispose() {
    _timer?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final total = widget.duration.inMilliseconds;
    final left = _remaining.inMilliseconds.clamp(0, total);
    final progress = total == 0 ? 0.0 : left / total;
    final mm = _remaining.inMinutes.remainder(60).toString().padLeft(2, '0');
    final ss = _remaining.inSeconds.remainder(60).toString().padLeft(2, '0');

    return Stack(
      alignment: Alignment.center,
      children: [
        CustomPaint(
          size: const Size.square(160),
          painter: _CountdownPainter(progress: progress),
        ),
        Text('$mm:$ss', style: BrainType.metric),
      ],
    );
  }
}

final class _AnalogClockPainter extends CustomPainter {
  _AnalogClockPainter({required this.now, required this.showSeconds});

  final DateTime now;
  final bool showSeconds;

  @override
  void paint(Canvas canvas, Size size) {
    final center = size.center(Offset.zero);
    final radius = size.shortestSide / 2;

    final face = Paint()..color = BrainPalette.surfaceSunken;
    final rim = Paint()
      ..color = BrainPalette.lineStrong
      ..style = PaintingStyle.stroke
      ..strokeWidth = 2;
    canvas.drawCircle(center, radius, face);
    canvas.drawCircle(center, radius - 1, rim);

    final tick = Paint()
      ..color = BrainPalette.textFaint
      ..strokeWidth = 1.5
      ..strokeCap = StrokeCap.round;
    for (var i = 0; i < 12; i++) {
      final angle = i * math.pi / 6 - math.pi / 2;
      final outer = Offset(
        center.dx + math.cos(angle) * (radius - 8),
        center.dy + math.sin(angle) * (radius - 8),
      );
      final inner = Offset(
        center.dx + math.cos(angle) * (radius - 16),
        center.dy + math.sin(angle) * (radius - 16),
      );
      canvas.drawLine(inner, outer, tick);
    }

    void hand(double angle, double length, Color color, double width) {
      final paint = Paint()
        ..color = color
        ..strokeWidth = width
        ..strokeCap = StrokeCap.round;
      canvas.drawLine(
        center,
        Offset(
          center.dx + math.cos(angle) * length,
          center.dy + math.sin(angle) * length,
        ),
        paint,
      );
    }

    final hourAngle =
        ((now.hour % 12) + now.minute / 60) * math.pi / 6 - math.pi / 2;
    final minuteAngle =
        (now.minute + now.second / 60) * math.pi / 30 - math.pi / 2;
    final secondAngle = now.second * math.pi / 30 - math.pi / 2;

    hand(hourAngle, radius * 0.45, BrainPalette.textPrimary, 3.5);
    hand(minuteAngle, radius * 0.62, BrainPalette.owner, 2.5);
    if (showSeconds) {
      hand(secondAngle, radius * 0.72, BrainPalette.signal, 1.4);
    }
    canvas.drawCircle(center, 3.5, Paint()..color = BrainPalette.signal);
  }

  @override
  bool shouldRepaint(covariant _AnalogClockPainter oldDelegate) =>
      oldDelegate.now != now || oldDelegate.showSeconds != showSeconds;
}

final class _CountdownPainter extends CustomPainter {
  _CountdownPainter({required this.progress});

  final double progress;

  @override
  void paint(Canvas canvas, Size size) {
    final center = size.center(Offset.zero);
    final radius = size.shortestSide / 2 - 6;
    final track = Paint()
      ..color = BrainPalette.line
      ..style = PaintingStyle.stroke
      ..strokeWidth = 8
      ..strokeCap = StrokeCap.round;
    final arc = Paint()
      ..color = BrainPalette.signal
      ..style = PaintingStyle.stroke
      ..strokeWidth = 8
      ..strokeCap = StrokeCap.round;

    canvas.drawCircle(center, radius, track);
    canvas.drawArc(
      Rect.fromCircle(center: center, radius: radius),
      -math.pi / 2,
      2 * math.pi * progress,
      false,
      arc,
    );
  }

  @override
  bool shouldRepaint(covariant _CountdownPainter oldDelegate) =>
      oldDelegate.progress != progress;
}

