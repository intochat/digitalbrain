import 'dart:async';
import 'dart:math' as math;
import 'dart:ui';

import 'package:flutter/material.dart';

import '../brain_theme.dart';

/// Offline UI-kit gallery for developers. No backend, no C# — pure Flutter demos.
final class KitScreen extends StatelessWidget {
  const KitScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return ColoredBox(
      key: const Key('kit_screen'),
      color: BrainPalette.surface,
      child: Align(
        alignment: Alignment.topCenter,
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 1100),
          child: ListView(
            padding: const EdgeInsets.fromLTRB(32, 28, 32, 48),
            children: const [
              _SectionHeader(
                title: 'UI Kit',
                subtitle:
                    'Design-system samples for shell chrome. Offline demo only.',
              ),
              SizedBox(height: 28),
              _PaletteSection(),
              SizedBox(height: 28),
              _SurfacesSection(),
              SizedBox(height: 28),
              _ControlsSection(),
              SizedBox(height: 28),
              _MetricsSection(),
              SizedBox(height: 28),
              _ClocksSection(),
              SizedBox(height: 28),
              _CardsSection(),
            ],
          ),
        ),
      ),
    );
  }
}

final class _SectionHeader extends StatelessWidget {
  const _SectionHeader({required this.title, required this.subtitle});

  final String title;
  final String subtitle;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(title, style: BrainType.heading),
        const SizedBox(height: 8),
        Text(subtitle, style: BrainType.bodyMuted),
      ],
    );
  }
}

final class _GroupLabel extends StatelessWidget {
  const _GroupLabel(this.text);

  final String text;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 12),
      child: Text(text.toUpperCase(), style: BrainType.metaStrong),
    );
  }
}

final class _PaletteSection extends StatelessWidget {
  const _PaletteSection();

  @override
  Widget build(BuildContext context) {
    const chips = <(String, Color)>[
      ('navigation', BrainPalette.navigation),
      ('surface', BrainPalette.surface),
      ('raised', BrainPalette.surfaceRaised),
      ('sunken', BrainPalette.surfaceSunken),
      ('line', BrainPalette.line),
      ('signal', BrainPalette.signal),
      ('owner', BrainPalette.owner),
      ('success', BrainPalette.success),
    ];

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const _GroupLabel('Palette'),
        Wrap(
          spacing: 12,
          runSpacing: 12,
          children: [
            for (final chip in chips)
              _Swatch(label: chip.$1, color: chip.$2),
          ],
        ),
      ],
    );
  }
}

final class _Swatch extends StatelessWidget {
  const _Swatch({required this.label, required this.color});

  final String label;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: 112,
      padding: const EdgeInsets.all(10),
      decoration: BoxDecoration(
        color: BrainPalette.surfaceRaised,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: BrainPalette.line),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Container(
            height: 36,
            decoration: BoxDecoration(
              color: color,
              borderRadius: BorderRadius.circular(8),
              border: Border.all(color: BrainPalette.lineStrong),
            ),
          ),
          const SizedBox(height: 8),
          Text(label, style: BrainType.meta),
        ],
      ),
    );
  }
}

final class _SurfacesSection extends StatelessWidget {
  const _SurfacesSection();

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const _GroupLabel('Surfaces'),
        Wrap(
          spacing: 16,
          runSpacing: 16,
          children: const [
            _GlassCard(
              title: 'Raised panel',
              body: 'Default chrome for lists, drawers, and studio panes.',
            ),
            _GlassCard(
              title: 'Glass blur',
              body: 'Backdrop blur + translucent fill for floating layers.',
              blurred: true,
            ),
            _GlassCard(
              title: 'Signal edge',
              body: 'Accent border for focused or live surfaces.',
              accent: true,
            ),
          ],
        ),
      ],
    );
  }
}

final class _GlassCard extends StatelessWidget {
  const _GlassCard({
    required this.title,
    required this.body,
    this.blurred = false,
    this.accent = false,
  });

  final String title;
  final String body;
  final bool blurred;
  final bool accent;

  @override
  Widget build(BuildContext context) {
    final child = Container(
      width: 260,
      padding: const EdgeInsets.all(18),
      decoration: BoxDecoration(
        color: BrainPalette.surfaceRaised.withValues(alpha: blurred ? 0.72 : 1),
        borderRadius: BorderRadius.circular(16),
        border: Border.all(
          color: accent
              ? BrainPalette.signal.withValues(alpha: 0.45)
              : BrainPalette.line,
        ),
        boxShadow: [
          BoxShadow(
            color: Colors.black.withValues(alpha: 0.28),
            blurRadius: 18,
            offset: const Offset(0, 10),
          ),
        ],
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(title, style: BrainType.cardTitle),
          const SizedBox(height: 8),
          Text(body, style: BrainType.bodyMuted),
        ],
      ),
    );

    if (!blurred) return child;

    return ClipRRect(
      borderRadius: BorderRadius.circular(16),
      child: BackdropFilter(
        filter: ImageFilter.blur(sigmaX: 14, sigmaY: 14),
        child: child,
      ),
    );
  }
}

final class _ControlsSection extends StatelessWidget {
  const _ControlsSection();

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const _GroupLabel('Controls'),
        Wrap(
          spacing: 12,
          runSpacing: 12,
          crossAxisAlignment: WrapCrossAlignment.center,
          children: [
            FilledButton(
              onPressed: () {},
              style: FilledButton.styleFrom(
                backgroundColor: BrainPalette.signal,
                foregroundColor: BrainPalette.surface,
              ),
              child: const Text('Primary'),
            ),
            FilledButton.tonal(
              onPressed: () {},
              child: const Text('Tonal'),
            ),
            OutlinedButton(
              onPressed: () {},
              child: const Text('Outlined'),
            ),
            TextButton(
              onPressed: () {},
              child: const Text('Text'),
            ),
            const _ToneBadge(label: 'live', color: BrainPalette.success),
            const _ToneBadge(label: 'owner', color: BrainPalette.owner),
            const _ToneBadge(label: 'signal', color: BrainPalette.signal),
            IconButton(
              onPressed: () {},
              icon: const Icon(Icons.hub_outlined),
              tooltip: 'Topology',
            ),
            IconButton.filled(
              onPressed: () {},
              icon: const Icon(Icons.graphic_eq_rounded),
              style: IconButton.styleFrom(
                backgroundColor: BrainPalette.signal.withValues(alpha: 0.16),
                foregroundColor: BrainPalette.signal,
              ),
            ),
          ],
        ),
      ],
    );
  }
}

final class _ToneBadge extends StatelessWidget {
  const _ToneBadge({required this.label, required this.color});

  final String label;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 5),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.14),
        borderRadius: BorderRadius.circular(999),
        border: Border.all(color: color.withValues(alpha: 0.35)),
      ),
      child: Text(
        label,
        style: BrainType.metaStrong.copyWith(color: color),
      ),
    );
  }
}

final class _MetricsSection extends StatelessWidget {
  const _MetricsSection();

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const _GroupLabel('Metrics'),
        Wrap(
          spacing: 12,
          runSpacing: 12,
          children: const [
            _MetricTile(label: 'neurons', value: '24', tone: BrainPalette.owner),
            _MetricTile(label: 'synapses / min', value: '186', tone: BrainPalette.signal),
            _MetricTile(label: 'latency p50', value: '12ms', tone: BrainPalette.success),
            _MetricTile(label: 'open windows', value: '4', tone: BrainPalette.textMuted),
          ],
        ),
      ],
    );
  }
}

final class _MetricTile extends StatelessWidget {
  const _MetricTile({
    required this.label,
    required this.value,
    required this.tone,
  });

  final String label;
  final String value;
  final Color tone;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: 180,
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: BrainPalette.surfaceRaised,
        borderRadius: BorderRadius.circular(14),
        border: Border.all(color: BrainPalette.line),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(label.toUpperCase(), style: BrainType.meta),
          const SizedBox(height: 10),
          Text(value, style: BrainType.metric.copyWith(color: tone)),
        ],
      ),
    );
  }
}

final class _ClocksSection extends StatelessWidget {
  const _ClocksSection();

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const _GroupLabel('Clocks'),
        Wrap(
          spacing: 16,
          runSpacing: 16,
          children: const [
            _ClockDemoCard(
              title: 'Analog',
              child: SizedBox(width: 160, height: 160, child: KitAnalogClock()),
            ),
            _ClockDemoCard(
              title: 'Countdown',
              child: SizedBox(
                width: 160,
                height: 160,
                child: KitCountdownClock(duration: Duration(minutes: 2)),
              ),
            ),
          ],
        ),
      ],
    );
  }
}

final class _ClockDemoCard extends StatelessWidget {
  const _ClockDemoCard({required this.title, required this.child});

  final String title;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(18),
      decoration: BoxDecoration(
        color: BrainPalette.surfaceRaised,
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: BrainPalette.line),
      ),
      child: Column(
        children: [
          Text(title, style: BrainType.cardTitle),
          const SizedBox(height: 14),
          child,
        ],
      ),
    );
  }
}

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

final class _CardsSection extends StatelessWidget {
  const _CardsSection();

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const _GroupLabel('Sample cards'),
        Wrap(
          spacing: 16,
          runSpacing: 16,
          children: const [
            _SampleCard(
              icon: Icons.flight_takeoff,
              title: 'Flight option',
              lines: ['LHR → NRT', 'Direct · 11h 40m', '£648'],
            ),
            _SampleCard(
              icon: Icons.hotel_outlined,
              title: 'Hotel stay',
              lines: ['Shinjuku Park', '3 nights · king', '£214 / night'],
            ),
            _SampleCard(
              icon: Icons.bolt_outlined,
              title: 'Synapse pulse',
              lines: ['ChatTurnCommitted', 'owner → assistant', 'seq 42'],
            ),
          ],
        ),
      ],
    );
  }
}

final class _SampleCard extends StatelessWidget {
  const _SampleCard({
    required this.icon,
    required this.title,
    required this.lines,
  });

  final IconData icon;
  final String title;
  final List<String> lines;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: 240,
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: BrainPalette.surfaceRaised,
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: BrainPalette.line),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Container(
                width: 34,
                height: 34,
                decoration: BoxDecoration(
                  color: BrainPalette.signal.withValues(alpha: 0.14),
                  borderRadius: BorderRadius.circular(10),
                ),
                child: Icon(icon, size: 18, color: BrainPalette.signal),
              ),
              const SizedBox(width: 10),
              Expanded(child: Text(title, style: BrainType.cardTitle)),
            ],
          ),
          const SizedBox(height: 14),
          for (final line in lines)
            Padding(
              padding: const EdgeInsets.only(bottom: 4),
              child: Text(line, style: BrainType.bodyMuted),
            ),
        ],
      ),
    );
  }
}
