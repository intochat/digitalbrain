import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../state/timeline_bloc.dart';
import 'shell_theme.dart';

const _chips = <(String label, String id)>[
  ('Now', 'now'),
  ('Last 10m', '10m'),
  ('Today', 'today'),
  ('This week', 'week'),
  ('Origin', 'origin'),
];

class ShellTimeline extends StatefulWidget {
  const ShellTimeline({super.key, this.onPinMoment});

  /// Tapping the "pin moment" button. The screen wires this to drop a
  /// gold mark at the current scrubber position; for slice 1 it can also
  /// be a no-op while the storyboard owns the mark list.
  final VoidCallback? onPinMoment;

  @override
  State<ShellTimeline> createState() => _ShellTimelineState();
}

class _ShellTimelineState extends State<ShellTimeline> {
  String _activeChip = 'now';
  double _scrubberX = 0.92; // proportion 0..1 along the river width

  @override
  Widget build(BuildContext context) {
    return BlocBuilder<TimelineBloc, TimelineBlocState>(
      buildWhen: (a, b) =>
          a.density != b.density || a.lifeMarks != b.lifeMarks,
      builder: (context, state) {
        return SizedBox(
          height: 138,
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              _ChipRow(
                active: _activeChip,
                onSelect: (id) {
                  setState(() {
                    _activeChip = id;
                    _scrubberX = _chipToX(id);
                  });
                },
                onPin: widget.onPinMoment,
              ),
              const SizedBox(height: 6),
              Expanded(
                child: GestureDetector(
                  behavior: HitTestBehavior.opaque,
                  onHorizontalDragUpdate: (event) {
                    final box = context.findRenderObject() as RenderBox?;
                    if (box == null) return;
                    final local = box.globalToLocal(event.globalPosition);
                    final w = box.size.width;
                    setState(() {
                      _scrubberX = (local.dx / w).clamp(0.0, 1.0);
                    });
                  },
                  child: CustomPaint(
                    painter: _RiverPainter(
                      density: state.density,
                      lifeMarks: state.lifeMarks,
                      scrubberX: _scrubberX,
                    ),
                    size: Size.infinite,
                  ),
                ),
              ),
            ],
          ),
        );
      },
    );
  }

  static double _chipToX(String id) => switch (id) {
        'origin' => 0.06,
        '10m' => 0.86,
        'today' => 0.62,
        'week' => 0.42,
        _ => 0.92, // 'now'
      };
}

class _ChipRow extends StatelessWidget {
  const _ChipRow({
    required this.active,
    required this.onSelect,
    required this.onPin,
  });

  final String active;
  final ValueChanged<String> onSelect;
  final VoidCallback? onPin;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 24),
      child: Row(
        children: [
          const Text(
            'TIMELINE',
            style: TextStyle(
              fontFamily: 'JetBrains Mono',
              fontSize: 10,
              letterSpacing: 1.8,
              color: InoShellTheme.muted2,
            ),
          ),
          const SizedBox(width: 12),
          for (final chip in _chips) ...[
            _Chip(
              label: chip.$1,
              active: active == chip.$2,
              onTap: () => onSelect(chip.$2),
            ),
            const SizedBox(width: 6),
          ],
          const Spacer(),
          const Text(
            '2026-05-07 · 14:32 · now',
            style: TextStyle(
              fontFamily: 'JetBrains Mono',
              fontSize: 11,
              color: InoShellTheme.muted,
            ),
          ),
          const SizedBox(width: 8),
          _PinButton(onPressed: onPin),
        ],
      ),
    );
  }
}

class _Chip extends StatelessWidget {
  const _Chip({
    required this.label,
    required this.active,
    required this.onTap,
  });

  final String label;
  final bool active;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return GestureDetector(
      onTap: onTap,
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 6),
        decoration: BoxDecoration(
          color: active
              ? InoShellTheme.cyan.withValues(alpha: 0.06)
              : Colors.white.withValues(alpha: 0.02),
          border: Border.all(
            color: active
                ? InoShellTheme.cyan.withValues(alpha: 0.5)
                : InoShellTheme.line,
          ),
          borderRadius: BorderRadius.circular(999),
        ),
        child: Text(
          label.toUpperCase(),
          style: TextStyle(
            fontSize: 11,
            letterSpacing: 0.4,
            color: active ? InoShellTheme.cyan : InoShellTheme.muted,
          ),
        ),
      ),
    );
  }
}

class _PinButton extends StatelessWidget {
  const _PinButton({required this.onPressed});

  final VoidCallback? onPressed;

  @override
  Widget build(BuildContext context) {
    return TextButton.icon(
      onPressed: onPressed,
      style: TextButton.styleFrom(
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
        backgroundColor: InoShellTheme.glassFill,
        side: const BorderSide(color: InoShellTheme.line),
        shape: const StadiumBorder(),
        foregroundColor: InoShellTheme.text,
      ),
      icon: Container(
        width: 6,
        height: 6,
        decoration: BoxDecoration(
          color: InoShellTheme.gold,
          shape: BoxShape.circle,
          boxShadow: [
            BoxShadow(color: InoShellTheme.gold, blurRadius: 8),
          ],
        ),
      ),
      label: const Text(
        'pin moment',
        style: TextStyle(fontSize: 11),
      ),
    );
  }
}

class _RiverPainter extends CustomPainter {
  _RiverPainter({
    required this.density,
    required this.lifeMarks,
    required this.scrubberX,
  });

  final List<double> density;
  final List<TimelineMark> lifeMarks;
  final double scrubberX;

  @override
  void paint(Canvas canvas, Size size) {
    _paintCenterline(canvas, size);
    if (density.isNotEmpty) _paintRiver(canvas, size);
    _paintLifeMarks(canvas, size);
    _paintScrubber(canvas, size);
  }

  void _paintCenterline(Canvas canvas, Size size) {
    canvas.drawLine(
      Offset(0, size.height / 2),
      Offset(size.width, size.height / 2),
      Paint()
        ..color = InoShellTheme.indigo.withValues(alpha: 0.18)
        ..strokeWidth = 1,
    );
  }

  // JS reference uses a mirrored band (peaks top + bottom). Flutter
  // translates it to the same mirrored shape: top arc above center,
  // bottom arc below center, closed as one filled path.
  void _paintRiver(Canvas canvas, Size size) {
    final half = size.height / 2;
    final n = density.length;
    final path = Path()..moveTo(0, half);

    for (var i = 0; i < n; i++) {
      final x = (i / (n - 1)) * size.width;
      final v = density[i].clamp(0.0, 1.0);
      final yTop = half * (1 - v); // above center
      path.lineTo(x, yTop);
    }

    for (var i = n - 1; i >= 0; i--) {
      final x = (i / (n - 1)) * size.width;
      final v = density[i].clamp(0.0, 1.0);
      final yBot = size.height - half * (1 - v); // below center
      path.lineTo(x, yBot);
    }
    path.close();

    final shader = const LinearGradient(
      begin: Alignment.topCenter,
      end: Alignment.bottomCenter,
      colors: [
        Color(0x997C8AFF),
        Color(0x2E3DDCFF),
        Color(0x003DDCFF),
      ],
      stops: [0.0, 0.6, 1.0],
    ).createShader(Rect.fromLTWH(0, 0, size.width, size.height));

    canvas.drawPath(path, Paint()..shader = shader);
  }

  void _paintLifeMarks(Canvas canvas, Size size) {
    for (final mark in lifeMarks) {
      if (mark.kind == 'now') continue; // now is the scrubber itself
      final cx = mark.x.clamp(0.0, 1.0) * size.width;
      final cy = size.height / 2;
      final color = switch (mark.kind) {
        'origin' => const Color(0xFFC9D6FF),
        'green' => const Color(0xFF6EE7A8),
        'gold' => InoShellTheme.gold,
        'red' => InoShellTheme.red,
        _ => InoShellTheme.indigo,
      };
      final radius = mark.kind == 'origin' ? 4.0 : 3.0;

      // Glow halo
      canvas.drawCircle(
        Offset(cx, cy),
        radius + 5,
        Paint()
          ..color = color.withValues(alpha: 0.3)
          ..maskFilter = const MaskFilter.blur(BlurStyle.normal, 4),
      );
      // Solid dot
      canvas.drawCircle(Offset(cx, cy), radius, Paint()..color = color);
    }
  }

  void _paintScrubber(Canvas canvas, Size size) {
    final cx = scrubberX.clamp(0.0, 1.0) * size.width;

    // Vertical line with gradient fade at top and bottom edges.
    final lineShader = const LinearGradient(
      begin: Alignment.topCenter,
      end: Alignment.bottomCenter,
      colors: [
        Color(0x003DDCFF),
        Color(0xD93DDCFF),
        Color(0xD93DDCFF),
        Color(0x003DDCFF),
      ],
      stops: [0.0, 0.2, 0.8, 1.0],
    ).createShader(Rect.fromLTWH(cx, 0, 1, size.height));

    canvas.drawLine(
      Offset(cx, 0),
      Offset(cx, size.height),
      Paint()
        ..shader = lineShader
        ..strokeWidth = 1,
    );

    // Draggable handle — cyan rounded rect at vertical center.
    const handleW = 14.0;
    const handleH = 36.0;
    final handleRect = RRect.fromRectAndRadius(
      Rect.fromCenter(
        center: Offset(cx, size.height / 2),
        width: handleW,
        height: handleH,
      ),
      const Radius.circular(6),
    );

    final handleShader = const LinearGradient(
      begin: Alignment.topCenter,
      end: Alignment.bottomCenter,
      colors: [Color(0xFF6CF5FF), Color(0xFF2BC4FF)],
    ).createShader(Rect.fromCenter(
      center: Offset(cx, size.height / 2),
      width: handleW,
      height: handleH,
    ));

    // Drop shadow beneath the handle.
    canvas.drawRRect(
      handleRect.shift(const Offset(0, 3)),
      Paint()
        ..color = const Color(0x723DDCFF)
        ..maskFilter = const MaskFilter.blur(BlurStyle.normal, 8),
    );
    // Inset highlight stroke.
    canvas.drawRRect(
      handleRect,
      Paint()
        ..color = Colors.white.withValues(alpha: 0.5)
        ..style = PaintingStyle.stroke
        ..strokeWidth = 0.5,
    );
    // Filled gradient body.
    canvas.drawRRect(handleRect, Paint()..shader = handleShader);
  }

  @override
  bool shouldRepaint(_RiverPainter old) =>
      old.density != density ||
      old.lifeMarks != lifeMarks ||
      old.scrubberX != scrubberX;
}
