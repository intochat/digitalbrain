// Tier-1 widget-canvas palette primitives (docs/redesign/03-WIDGET-PALETTE.md).
//
// These are the new host-owned RFW widgets the redesign adds on top of the
// existing 92-widget dictionary. Each reads Theme.of(context) / DigitalBrainColors
// so server-emitted (.ino `rfw:`) surfaces inherit the visual language, takes all
// variable inputs from RFW `data`, and fires user actions as RFW `event`s.
// Registering them is the one batched binary rebuild (Tier 1); after that every
// arrangement is pure Tier-2 data.

import 'dart:async';
import 'dart:convert';

import 'package:flutter/foundation.dart' show kIsWeb;
import 'package:flutter/material.dart';
import 'package:flutter_earth_globe/flutter_earth_globe.dart';
import 'package:flutter_earth_globe/flutter_earth_globe_controller.dart';
import 'package:flutter_earth_globe/globe_coordinates.dart';
import 'package:flutter_earth_globe/point.dart';
import 'package:flutter_earth_globe/point_connection.dart';
import 'package:flutter_earth_globe/point_connection_style.dart';
import 'package:lottie/lottie.dart';
import 'package:rfw/rfw.dart';

import 'package:digitalbrain_flutter/theme/digitalbrain_theme.dart';

// ── DataSource readers (local copies so the palette stays self-contained) ──
double _d(DataSource s, String k, double def) =>
    s.v<double>([k]) ?? s.v<int>([k])?.toDouble() ?? def;
String _s(DataSource s, String k, [String def = '']) => s.v<String>([k]) ?? def;
int _i(DataSource s, String k, int def) =>
    s.v<int>([k]) ?? s.v<double>([k])?.toInt() ?? def;
bool _b(DataSource s, String k, bool def) => s.v<bool>([k]) ?? def;

double _dp(DataSource s, List<Object> p, double def) =>
    s.v<double>(p) ?? s.v<int>(p)?.toDouble() ?? def;
String _sp(DataSource s, List<Object> p, [String def = '']) =>
    s.v<String>(p) ?? def;

// ─────────────────────────────────────────────────────────────────────────
// LottiePlayer
// ─────────────────────────────────────────────────────────────────────────
Widget lottiePlayer(BuildContext context, DataSource source) {
  return _LottiePlayer(
    src: _s(source, 'src'),
    loop: _b(source, 'loop', true),
    autoplay: _b(source, 'autoplay', true),
    speed: _d(source, 'speed', 1.0),
  );
}

class _LottiePlayer extends StatefulWidget {
  const _LottiePlayer({
    required this.src,
    required this.loop,
    required this.autoplay,
    required this.speed,
  });

  final String src;
  final bool loop;
  final bool autoplay;
  final double speed;

  @override
  State<_LottiePlayer> createState() => _LottiePlayerState();
}

class _LottiePlayerState extends State<_LottiePlayer>
    with TickerProviderStateMixin {
  late final AnimationController _controller = AnimationController(vsync: this);

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  void _onLoaded(LottieComposition composition) {
    _controller.duration =
        composition.duration * (1 / widget.speed.clamp(0.05, 20));
    if (!widget.autoplay) return;
    if (widget.loop) {
      _controller.repeat();
    } else {
      _controller.forward();
    }
  }

  @override
  Widget build(BuildContext context) {
    final src = widget.src;
    if (src.isEmpty) return const SizedBox.shrink();
    final isNetwork = src.startsWith('http://') || src.startsWith('https://');
    return isNetwork
        ? Lottie.network(src, controller: _controller, onLoaded: _onLoaded)
        : Lottie.asset(src, controller: _controller, onLoaded: _onLoaded);
  }
}

// ─────────────────────────────────────────────────────────────────────────
// AnalogClock — self-driving 1s ticker; no data round-trips for the tick.
// ─────────────────────────────────────────────────────────────────────────
Widget analogClock(BuildContext context, DataSource source) {
  return _AnalogClock(
    showSeconds: _b(source, 'showSeconds', true),
    face: _s(source, 'face', 'minimal'),
    offsetMinutes: _i(source, 'offsetMinutes', 0),
  );
}

class _AnalogClock extends StatefulWidget {
  const _AnalogClock({
    required this.showSeconds,
    required this.face,
    required this.offsetMinutes,
  });

  final bool showSeconds;
  final String face;
  final int offsetMinutes;

  @override
  State<_AnalogClock> createState() => _AnalogClockState();
}

class _AnalogClockState extends State<_AnalogClock> {
  Timer? _ticker;
  DateTime _now = DateTime.now();

  @override
  void initState() {
    super.initState();
    _ticker = Timer.periodic(const Duration(seconds: 1), (_) {
      if (mounted) setState(() => _now = DateTime.now());
    });
  }

  @override
  void dispose() {
    _ticker?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return AspectRatio(
      aspectRatio: 1,
      child: CustomPaint(
        painter: _ClockPainter(
          time: _now.add(Duration(minutes: widget.offsetMinutes)),
          showSeconds: widget.showSeconds,
          numerals: widget.face == 'numerals',
          accent: DigitalBrainColors.tealSoft,
          backward: false,
          progress: 0,
        ),
      ),
    );
  }
}

// ─────────────────────────────────────────────────────────────────────────
// CountdownClock — the reminder primitive: hands run BACKWARD toward zero.
// Drift-free remaining = duration - (now - startedAt). Fires `onZero` once.
// ─────────────────────────────────────────────────────────────────────────
Widget countdownClock(BuildContext context, DataSource source) {
  return _CountdownClock(
    durationSeconds: _i(source, 'durationSeconds', 600),
    startedAtUtc: _s(source, 'startedAtUtc'),
    onZero: source.voidHandler(['onZero']),
    onSnooze: source.voidHandler(['onSnooze']),
  );
}

class _CountdownClock extends StatefulWidget {
  const _CountdownClock({
    required this.durationSeconds,
    required this.startedAtUtc,
    required this.onZero,
    required this.onSnooze,
  });

  final int durationSeconds;
  final String startedAtUtc;
  final VoidCallback? onZero;
  final VoidCallback? onSnooze;

  @override
  State<_CountdownClock> createState() => _CountdownClockState();
}

class _CountdownClockState extends State<_CountdownClock>
    with SingleTickerProviderStateMixin {
  Timer? _ticker;
  late DateTime _startedAt;
  bool _fired = false;
  late final AnimationController _pulse = AnimationController(
    vsync: this,
    duration: const Duration(milliseconds: 700),
    lowerBound: 0.0,
    upperBound: 1.0,
  );

  @override
  void initState() {
    super.initState();
    _startedAt =
        DateTime.tryParse(widget.startedAtUtc)?.toUtc() ??
        DateTime.now().toUtc();
    _ticker = Timer.periodic(const Duration(seconds: 1), (_) {
      if (mounted) setState(() {});
      _maybeFire();
    });
  }

  Duration get _remaining {
    final elapsed = DateTime.now().toUtc().difference(_startedAt);
    final left = Duration(seconds: widget.durationSeconds) - elapsed;
    return left.isNegative ? Duration.zero : left;
  }

  void _maybeFire() {
    if (_fired || _remaining > Duration.zero) return;
    _fired = true;
    _pulse.repeat(reverse: true, count: 4);
    widget.onZero?.call();
  }

  @override
  void dispose() {
    _ticker?.cancel();
    _pulse.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final left = _remaining;
    final total = widget.durationSeconds <= 0 ? 1 : widget.durationSeconds;
    final progress = left.inMilliseconds / (total * 1000);
    final mm = left.inMinutes.remainder(60).toString().padLeft(2, '0');
    final ss = left.inSeconds.remainder(60).toString().padLeft(2, '0');
    final accent = left == Duration.zero
        ? DigitalBrainColors.rose
        : DigitalBrainColors.gold;

    final clock = AspectRatio(
      aspectRatio: 1,
      child: AnimatedBuilder(
        animation: _pulse,
        builder: (context, child) {
          final glow = 1.0 + _pulse.value * 0.06;
          return Transform.scale(scale: glow, child: child);
        },
        child: Stack(
          alignment: Alignment.center,
          children: [
            CustomPaint(
              size: Size.infinite,
              painter: _ClockPainter(
                time: DateTime.fromMillisecondsSinceEpoch(left.inMilliseconds),
                showSeconds: true,
                numerals: false,
                accent: accent,
                backward: true,
                progress: progress.clamp(0.0, 1.0),
              ),
            ),
            Text(
              '$mm:$ss',
              style: TextStyle(
                color: accent,
                fontSize: 18,
                fontWeight: FontWeight.w600,
                fontFeatures: const [FontFeature.tabularFigures()],
              ),
            ),
          ],
        ),
      ),
    );

    if (widget.onSnooze == null) return clock;
    return Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        Flexible(child: clock),
        const SizedBox(height: 6),
        TextButton.icon(
          onPressed: widget.onSnooze,
          style: TextButton.styleFrom(
            foregroundColor: DigitalBrainColors.gold,
            visualDensity: VisualDensity.compact,
          ),
          icon: const Icon(Icons.snooze, size: 16),
          label: const Text('Snooze 5m'),
        ),
      ],
    );
  }
}

// Shared painter for AnalogClock + CountdownClock. `backward` reverses the
// hand sweep so the countdown visibly unwinds; `progress` (0..1) draws the
// remaining-time ring.
class _ClockPainter extends CustomPainter {
  _ClockPainter({
    required this.time,
    required this.showSeconds,
    required this.numerals,
    required this.accent,
    required this.backward,
    required this.progress,
  });

  final DateTime time;
  final bool showSeconds;
  final bool numerals;
  final Color accent;
  final bool backward;
  final double progress;

  @override
  void paint(Canvas canvas, Size size) {
    final center = Offset(size.width / 2, size.height / 2);
    final radius = size.shortestSide / 2 - 6;

    final face = Paint()
      ..style = PaintingStyle.fill
      ..color = const Color(0xFF12141A);
    canvas.drawCircle(center, radius, face);

    final rim = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 2
      ..color = accent.withValues(alpha: 0.35);
    canvas.drawCircle(center, radius, rim);

    if (progress > 0) {
      final ring = Paint()
        ..style = PaintingStyle.stroke
        ..strokeWidth = 4
        ..strokeCap = StrokeCap.round
        ..color = accent;
      canvas.drawArc(
        Rect.fromCircle(center: center, radius: radius - 3),
        -1.5708,
        6.28318 * progress,
        false,
        ring,
      );
    }

    for (var t = 0; t < 12; t++) {
      final a = t * 0.5236;
      final outer = center + Offset.fromDirection(a, radius - 4);
      final inner = center + Offset.fromDirection(a, radius - 12);
      canvas.drawLine(
        inner,
        outer,
        Paint()
          ..strokeWidth = 1.5
          ..color = Colors.white.withValues(alpha: 0.25),
      );
    }

    final dir = backward ? -1.0 : 1.0;
    final seconds = time.second + time.millisecond / 1000.0;
    final minutes = time.minute + seconds / 60.0;
    final hours = (time.hour % 12) + minutes / 60.0;

    void hand(double turns, double length, double width, Color color) {
      final angle = -1.5708 + dir * turns * 6.28318;
      canvas.drawLine(
        center,
        center + Offset.fromDirection(angle, length),
        Paint()
          ..strokeWidth = width
          ..strokeCap = StrokeCap.round
          ..color = color,
      );
    }

    hand(hours / 12.0, radius * 0.5, 4, Colors.white.withValues(alpha: 0.85));
    hand(
      minutes / 60.0,
      radius * 0.72,
      3,
      Colors.white.withValues(alpha: 0.85),
    );
    if (showSeconds) hand(seconds / 60.0, radius * 0.82, 1.5, accent);

    canvas.drawCircle(center, 3, Paint()..color = accent);
  }

  @override
  bool shouldRepaint(_ClockPainter old) =>
      old.time != time || old.progress != progress || old.accent != accent;
}

// ─────────────────────────────────────────────────────────────────────────
// EarthGlobe — GPU/shader 3D globe (needs `--wasm` on web). Points + animated
// dashed connection arcs come straight from RFW data.
// ─────────────────────────────────────────────────────────────────────────
Widget earthGlobe(BuildContext context, DataSource source) {
  final points = <GlobePoint>[];
  final pointCount = source.length(['points']);
  for (var i = 0; i < pointCount; i++) {
    points.add(
      GlobePoint(
        lat: _dp(source, ['points', i, 'lat'], 0),
        lng: _dp(source, ['points', i, 'lng'], 0),
        label: _sp(source, ['points', i, 'label']),
      ),
    );
  }

  final arcs = <GlobeArc>[];
  final arcCount = source.length(['arcs']);
  for (var i = 0; i < arcCount; i++) {
    arcs.add(
      GlobeArc(
        fromLat: _dp(source, ['arcs', i, 'from', 'lat'], 0),
        fromLng: _dp(source, ['arcs', i, 'from', 'lng'], 0),
        toLat: _dp(source, ['arcs', i, 'to', 'lat'], 0),
        toLng: _dp(source, ['arcs', i, 'to', 'lng'], 0),
        dashed: _sp(source, ['arcs', i, 'style']) != 'solid',
      ),
    );
  }

  // Slice-0 gate (docs/redesign/05-ROADMAP.md): the shader-based 3D globe paints
  // nothing under the web `--wasm`/skwasm renderer (Lottie + clocks are fine).
  // On web fall back to a flat animated route map that consumes the same
  // points/arcs data; the real globe ships on flutter-windows.
  if (kIsWeb) {
    return _EarthGlobeFlat(points: points, arcs: arcs);
  }

  return _EarthGlobe(
    points: points,
    arcs: arcs,
    autoRotate: _b(source, 'autoRotate', true),
  );
}

class GlobePoint {
  const GlobePoint({required this.lat, required this.lng, required this.label});
  final double lat;
  final double lng;
  final String label;
}

class GlobeArc {
  const GlobeArc({
    required this.fromLat,
    required this.fromLng,
    required this.toLat,
    required this.toLng,
    required this.dashed,
  });
  final double fromLat;
  final double fromLng;
  final double toLat;
  final double toLng;
  final bool dashed;
}

class _EarthGlobe extends StatefulWidget {
  const _EarthGlobe({
    required this.points,
    required this.arcs,
    required this.autoRotate,
  });

  final List<GlobePoint> points;
  final List<GlobeArc> arcs;
  final bool autoRotate;

  @override
  State<_EarthGlobe> createState() => _EarthGlobeState();
}

class _EarthGlobeState extends State<_EarthGlobe> {
  // 1x1 deep-ocean-blue PNG stretched as an equirectangular surface — keeps the
  // primitive asset-free; a domain can ship a real texture later via Tier-2.
  static final ImageProvider _surface = MemoryImage(
    base64Decode(
      'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==',
    ),
  );

  late final FlutterEarthGlobeController _controller;

  @override
  void initState() {
    super.initState();
    _controller = FlutterEarthGlobeController(
      rotationSpeed: 0.06,
      isRotating: widget.autoRotate,
      isBackgroundFollowingSphereRotation: true,
      surface: _surface,
    );
    _controller.onLoaded = _populate;
  }

  void _populate() {
    for (var i = 0; i < widget.points.length; i++) {
      final p = widget.points[i];
      _controller.addPoint(
        Point(
          id: 'p$i',
          coordinates: GlobeCoordinates(p.lat, p.lng),
          label: p.label,
          isLabelVisible: p.label.isNotEmpty,
          style: PointStyle(color: DigitalBrainColors.tealSoft, size: 6),
        ),
      );
    }
    for (var i = 0; i < widget.arcs.length; i++) {
      final a = widget.arcs[i];
      _controller.addPointConnection(
        PointConnection(
          id: 'a$i',
          start: GlobeCoordinates(a.fromLat, a.fromLng),
          end: GlobeCoordinates(a.toLat, a.toLng),
          isMoving: true,
          isLabelVisible: false,
          style: PointConnectionStyle(
            type: a.dashed
                ? PointConnectionType.dashed
                : PointConnectionType.solid,
            color: DigitalBrainColors.tealSoft,
            lineWidth: 2,
          ),
        ),
        animateDraw: true,
      );
    }
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return LayoutBuilder(
      builder: (context, constraints) {
        final radius =
            (constraints.biggest.shortestSide.clamp(80.0, 600.0)) / 2 - 8;
        return Center(
          child: FlutterEarthGlobe(controller: _controller, radius: radius),
        );
      },
    );
  }
}

// Web fallback for [_EarthGlobe]: the GPU globe is blank under skwasm, so on web
// the same points/arcs render as a flat equirectangular route map with an
// animated dashed great-circle-style arc. Desktop keeps the real 3D globe.
class _EarthGlobeFlat extends StatefulWidget {
  const _EarthGlobeFlat({required this.points, required this.arcs});

  final List<GlobePoint> points;
  final List<GlobeArc> arcs;

  @override
  State<_EarthGlobeFlat> createState() => _EarthGlobeFlatState();
}

class _EarthGlobeFlatState extends State<_EarthGlobeFlat>
    with SingleTickerProviderStateMixin {
  late final AnimationController _dash = AnimationController(
    vsync: this,
    duration: const Duration(seconds: 3),
  )..repeat();

  @override
  void dispose() {
    _dash.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.all(12),
      child: AnimatedBuilder(
        animation: _dash,
        builder: (context, _) => CustomPaint(
          painter: _FlatRoutePainter(
            points: widget.points,
            arcs: widget.arcs,
            phase: _dash.value,
            accent: DigitalBrainColors.tealSoft,
            active: DigitalBrainColors.rose,
          ),
          child: const SizedBox.expand(),
        ),
      ),
    );
  }
}

class _FlatRoutePainter extends CustomPainter {
  _FlatRoutePainter({
    required this.points,
    required this.arcs,
    required this.phase,
    required this.accent,
    required this.active,
  });

  final List<GlobePoint> points;
  final List<GlobeArc> arcs;
  final double phase;
  final Color accent;
  final Color active;

  // Equirectangular projection into a 2:1 map letterboxed inside [size].
  Rect _mapRect(Size size) {
    final w = size.width;
    final h = size.height;
    var mw = w;
    var mh = w / 2;
    if (mh > h) {
      mh = h;
      mw = h * 2;
    }
    final left = (w - mw) / 2;
    final top = (h - mh) / 2;
    return Rect.fromLTWH(left, top, mw, mh);
  }

  Offset _project(Rect r, double lat, double lng) => Offset(
    r.left + (lng + 180) / 360 * r.width,
    r.top + (90 - lat) / 180 * r.height,
  );

  @override
  void paint(Canvas canvas, Size size) {
    final r = _mapRect(size);
    final rr = RRect.fromRectAndRadius(r, const Radius.circular(12));
    canvas.drawRRect(rr, Paint()..color = DigitalBrainColors.obsidianSlate);
    canvas.drawRRect(
      rr,
      Paint()
        ..style = PaintingStyle.stroke
        ..strokeWidth = 1
        ..color = DigitalBrainColors.hairlineStrong,
    );

    canvas.save();
    canvas.clipRRect(rr);

    // Faint lat/lng graticule.
    final grid = Paint()
      ..color = DigitalBrainColors.hairline
      ..strokeWidth = 1;
    for (var lng = -180.0; lng <= 180; lng += 30) {
      final x = _project(r, 0, lng).dx;
      canvas.drawLine(Offset(x, r.top), Offset(x, r.bottom), grid);
    }
    for (var lat = -60.0; lat <= 60; lat += 30) {
      final y = _project(r, lat, 0).dy;
      canvas.drawLine(Offset(r.left, y), Offset(r.right, y), grid);
    }

    for (final a in arcs) {
      final from = _project(r, a.fromLat, a.fromLng);
      final to = _project(r, a.toLat, a.toLng);
      // Bow the path upward to suggest a great-circle flight track.
      final mid = Offset(
        (from.dx + to.dx) / 2,
        (from.dy + to.dy) / 2 - (to - from).distance * 0.22,
      );
      final path = Path()
        ..moveTo(from.dx, from.dy)
        ..quadraticBezierTo(mid.dx, mid.dy, to.dx, to.dy);
      final arcPaint = Paint()
        ..style = PaintingStyle.stroke
        ..strokeWidth = 2
        ..strokeCap = StrokeCap.round
        ..color = accent;
      if (a.dashed) {
        _drawDashed(canvas, path, arcPaint, phase);
      } else {
        canvas.drawPath(path, arcPaint);
      }
    }

    for (var i = 0; i < points.length; i++) {
      final p = points[i];
      final c = _project(r, p.lat, p.lng);
      // First and last endpoints get the amber "active" marker.
      final isEndpoint = i == 0 || i == points.length - 1;
      canvas.drawCircle(
        c,
        isEndpoint ? 5 : 3,
        Paint()..color = isEndpoint ? active : accent,
      );
      canvas.drawCircle(
        c,
        isEndpoint ? 5 : 3,
        Paint()
          ..style = PaintingStyle.stroke
          ..strokeWidth = 1
          ..color = DigitalBrainColors.pitchBlack,
      );
      if (p.label.isNotEmpty) {
        final tp = TextPainter(
          text: TextSpan(
            text: p.label,
            style: const TextStyle(
              color: DigitalBrainColors.inkMid,
              fontSize: 10,
            ),
          ),
          textDirection: TextDirection.ltr,
        )..layout();
        tp.paint(canvas, c + const Offset(7, -5));
      }
    }
    canvas.restore();
  }

  void _drawDashed(Canvas canvas, Path path, Paint paint, double phase) {
    const dash = 7.0;
    const gap = 6.0;
    final span = dash + gap;
    for (final metric in path.computeMetrics()) {
      var d = -((phase * span) % span); // march the dashes along the track
      while (d < metric.length) {
        final start = d.clamp(0.0, metric.length);
        final end = (d + dash).clamp(0.0, metric.length);
        if (end > start) {
          canvas.drawPath(metric.extractPath(start, end), paint);
        }
        d += span;
      }
    }
  }

  @override
  bool shouldRepaint(_FlatRoutePainter old) =>
      old.phase != phase || old.points != points || old.arcs != arcs;
}

// ─────────────────────────────────────────────────────────────────────────
// FloatingWindow — lets a surface DECLARE a preferred panel frame. The canvas
// shell normally draws the chrome; this primitive exists for standalone use
// and to honor a neuron-declared title / lock state (V4-5).
// ─────────────────────────────────────────────────────────────────────────
Widget floatingWindow(BuildContext context, DataSource source) {
  final lock = _s(source, 'lockState', 'idle');
  final lockColor = switch (lock) {
    'busy' => DigitalBrainColors.gold,
    'modal' => DigitalBrainColors.rose,
    _ => DigitalBrainColors.tealSoft,
  };
  return Container(
    decoration: BoxDecoration(
      color: const Color(0xFF12141A).withValues(alpha: 0.92),
      borderRadius: BorderRadius.circular(16),
      border: Border.all(color: lockColor.withValues(alpha: 0.4)),
    ),
    child: Column(
      mainAxisSize: MainAxisSize.min,
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(14, 10, 14, 8),
          child: Row(
            children: [
              Container(
                width: 8,
                height: 8,
                decoration: BoxDecoration(
                  color: lockColor,
                  shape: BoxShape.circle,
                ),
              ),
              const SizedBox(width: 8),
              Expanded(
                child: Text(
                  _s(source, 'title', 'Surface'),
                  style: const TextStyle(
                    color: Colors.white,
                    fontWeight: FontWeight.w600,
                  ),
                ),
              ),
            ],
          ),
        ),
        Flexible(
          child: Padding(
            padding: const EdgeInsets.fromLTRB(14, 0, 14, 14),
            child: source.optionalChild(['child']) ?? const SizedBox.shrink(),
          ),
        ),
      ],
    ),
  );
}
