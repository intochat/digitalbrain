import 'dart:math' as math;
import 'dart:ui' as ui;

import 'package:flutter/material.dart';

import 'rive_design_registry.dart';
import 'rive_handles.dart';

// ---------------------------------------------------------------------------
// Mood colour palette — shared by Hero, PersonaInline, and the log badges.
// ---------------------------------------------------------------------------

Color _moodColor(String mood) => switch (mood) {
      'discovering' => const Color(0xFF00BCD4),
      'happy' => const Color(0xFFFFB300),
      'rethinking' => const Color(0xFF7C4DFF),
      'thoughtful' => const Color(0xFF1976D2),
      'centered' => const Color(0xFF00897B),
      _ => const Color(0xFF90A4AE),
    };

List<Color> _moodGradient(String mood) => switch (mood) {
      'discovering' => [const Color(0xFF00BCD4), const Color(0xFF3F51B5)],
      'happy' => [const Color(0xFFFFB300), const Color(0xFFE91E63)],
      'rethinking' => [const Color(0xFF7C4DFF), const Color(0xFF607D8B)],
      'thoughtful' => [const Color(0xFF1976D2), const Color(0xFF00BCD4)],
      'centered' => [const Color(0xFF00897B), const Color(0xFF4CAF50)],
      _ => [const Color(0xFF546E7A), const Color(0xFF37474F)],
    };

// ---------------------------------------------------------------------------
// Registry
// ---------------------------------------------------------------------------

class DemoRiveDesignRegistry implements RiveDesignRegistry {
  // Kebab-keyed live handles — populated by resolveController, removed on dispose.
  final _handles = <String, List<_DemoVmHandle>>{};

  @override
  Future<RiveResolution> resolveController({
    required String domain,
    required String artboard,
  }) {
    // Microtask so callers that await inline don't block the first frame.
    return Future.value(_buildResolution(artboard));
  }

  RiveResolution _buildResolution(String artboard) {
    final vm = _DemoVmHandle();
    final key = _kebab(artboard);
    (_handles[key] ??= []).add(vm);
    final resolution = switch (artboard) {
      'Hero' => _DemoResolution(vm, _HeroArtboardWidget.new),
      'Tile' => _DemoResolution(vm, _TileArtboardWidget.new),
      'Badge' => _DemoResolution(vm, _BadgeArtboardWidget.new),
      'PersonaInline' =>
        _DemoResolution(vm, _PersonaInlineArtboardWidget.new),
      'Spacer' => _DemoResolution(vm, _SpacerArtboardWidget.new),
      _ => _DemoResolution(vm, _UnknownArtboardWidget.new),
    };
    // Remove handle when this resolution is disposed so we don't hold stale refs.
    resolution._onDispose = () => _handles[key]?.remove(vm);
    return resolution;
  }

  // Fires a named trigger on every live handle registered under artboardKey
  // (kebab-cased artboard name, e.g. 'persona-inline').
  void fireTrigger(String artboardKey, String triggerName) {
    final handles = _handles[artboardKey];
    if (handles == null) return;
    for (final h in List.of(handles)) {
      h.fireTrigger(triggerName);
    }
  }
}

// Converts PascalCase artboard names to kebab-case ('PersonaInline' → 'persona-inline').
String _kebab(String pascal) =>
    pascal.replaceAllMapped(RegExp(r'(?<=[a-z])([A-Z])'), (m) => '-${m[1]}').toLowerCase();

// ---------------------------------------------------------------------------
// Resolution + VmHandle
// ---------------------------------------------------------------------------

typedef _ArtboardBuilder = Widget Function(_DemoVmHandle vm);

class _DemoResolution implements RiveResolution {
  _DemoResolution(this._vm, this._builder);

  final _DemoVmHandle _vm;
  final _ArtboardBuilder _builder;
  bool _disposed = false;
  VoidCallback? _onDispose;

  @override
  ViewModelHandle get viewModel => _vm;

  @override
  Widget buildWidget() {
    assert(!_disposed, 'buildWidget called after dispose');
    return _builder(_vm);
  }

  @override
  void dispose() {
    _disposed = true;
    _onDispose?.call();
    _vm.dispose();
  }
}

// ---------------------------------------------------------------------------
// VmHandle — records writes into a ValueNotifier so widgets rebuild.
// ---------------------------------------------------------------------------

class _VmState {
  final strings = <String, String>{};
  final numbers = <String, double>{};
  final numberAnims = <String, AnimSpec?>{};
  final colors = <String, Color>{};
  final colorAnims = <String, AnimSpec?>{};
  final enums = <String, String>{};
  // trigger callbacks — multiple listeners allowed per trigger name
  final triggerCallbacks = <String, List<VoidCallback>>{};
  int version = 0;
}

class _DemoVmHandle implements ViewModelHandle {
  final _state = _VmState();
  final _notifier = ValueNotifier<int>(0);

  ValueNotifier<int> get notifier => _notifier;
  _VmState get state => _state;

  @override
  void writeString(String name, String value) {
    if (_state.strings[name] == value) return;
    _state.strings[name] = value;
    _bump();
  }

  @override
  void writeNumber(String name, double value, {AnimSpec? anim}) {
    if (_state.numbers[name] == value) return;
    _state.numbers[name] = value;
    _state.numberAnims[name] = anim;
    _bump();
  }

  @override
  void writeColor(String name, Color value, {AnimSpec? anim}) {
    if (_state.colors[name] == value) return;
    _state.colors[name] = value;
    _state.colorAnims[name] = anim;
    _bump();
  }

  @override
  void writeEnum(String name, String value) {
    if (_state.enums[name] == value) return;
    _state.enums[name] = value;
    _bump();
  }

  @override
  void onTrigger(String name, VoidCallback handler) {
    (_state.triggerCallbacks[name] ??= []).add(handler);
  }

  void fireTrigger(String name) {
    final callbacks = _state.triggerCallbacks[name];
    if (callbacks == null) return;
    for (final cb in List.of(callbacks)) {
      cb();
    }
  }

  void _bump() {
    _state.version++;
    _notifier.value = _state.version;
  }

  @override
  void dispose() {
    _notifier.dispose();
  }
}

// ---------------------------------------------------------------------------
// _TweenedNumber — listens to a VmHandle for one numeric field and exposes
// either the snapped value (no AnimSpec) or a curve-driven interpolation
// (AnimSpec set). Reusable across artboards that consume tweenable numbers.
// ---------------------------------------------------------------------------

class _TweenedNumber extends ChangeNotifier {
  _TweenedNumber({
    required this.vm,
    required this.field,
    required TickerProvider vsync,
  }) {
    _ctrl = AnimationController(vsync: vsync);
    _ctrl.addListener(_onTickerTick);
    _value = vm.state.numbers[field] ?? 0;
    vm.notifier.addListener(_onVmChanged);
  }

  final _DemoVmHandle vm;
  final String field;
  late final AnimationController _ctrl;
  Animation<double>? _animation;
  double _value = 0;

  double get value => _animation?.value ?? _value;

  void _onTickerTick() {
    notifyListeners();
  }

  void _onVmChanged() {
    final target = vm.state.numbers[field] ?? 0;
    if (target == _value) return;
    final spec = vm.state.numberAnims[field];
    final from = value;
    if (spec == null || spec.duration <= Duration.zero) {
      _value = target;
      _animation = null;
      _ctrl.stop();
      notifyListeners();
      return;
    }
    _value = target;
    _ctrl.duration = spec.duration;
    _animation = Tween<double>(begin: from, end: target).animate(
      CurvedAnimation(parent: _ctrl, curve: spec.curve),
    );
    _ctrl
      ..reset()
      ..forward();
  }

  @override
  void dispose() {
    vm.notifier.removeListener(_onVmChanged);
    _ctrl.removeListener(_onTickerTick);
    _ctrl.dispose();
    super.dispose();
  }
}

// ---------------------------------------------------------------------------
// Hero artboard widget
// ---------------------------------------------------------------------------

class _HeroArtboardWidget extends StatefulWidget {
  const _HeroArtboardWidget(this.vm);
  final _DemoVmHandle vm;

  @override
  State<_HeroArtboardWidget> createState() => _HeroArtboardWidgetState();
}

class _HeroArtboardWidgetState extends State<_HeroArtboardWidget>
    with SingleTickerProviderStateMixin {
  late final AnimationController _shimmer;

  @override
  void initState() {
    super.initState();
    _shimmer = AnimationController(
      vsync: this,
      duration: const Duration(seconds: 4),
    )..repeat();
  }

  @override
  void dispose() {
    _shimmer.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return ValueListenableBuilder<int>(
      valueListenable: widget.vm.notifier,
      builder: (context, snapshot, child) {
        final title = widget.vm.state.strings['title'] ?? '';
        final subtitle = widget.vm.state.strings['subtitle'] ?? '';
        final mood = widget.vm.state.strings['mood'] ?? 'discovering';
        final gradient = _moodGradient(mood);

        return AnimatedBuilder(
          animation: _shimmer,
          builder: (context, _) {
            return Container(
              height: 320,
              decoration: BoxDecoration(
                borderRadius: BorderRadius.circular(16),
                gradient: LinearGradient(
                  begin: Alignment.topLeft,
                  end: Alignment.bottomRight,
                  colors: gradient,
                ),
              ),
              child: ClipRRect(
                borderRadius: BorderRadius.circular(16),
                child: Stack(
                  children: [
                    // Animated glow orb
                    Positioned.fill(
                      child: Opacity(
                        opacity: 0.22,
                        child: CustomPaint(
                          painter: _NoisePainter(_shimmer.value, gradient[0]),
                        ),
                      ),
                    ),
                    // Content
                    Padding(
                      padding: const EdgeInsets.fromLTRB(24, 32, 24, 28),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        mainAxisAlignment: MainAxisAlignment.end,
                        children: [
                          AnimatedSwitcher(
                            duration: const Duration(milliseconds: 400),
                            transitionBuilder: (child, anim) =>
                                FadeTransition(
                                  opacity: anim,
                                  child: SlideTransition(
                                    position: Tween<Offset>(
                                      begin: const Offset(0, 0.15),
                                      end: Offset.zero,
                                    ).animate(anim),
                                    child: child,
                                  ),
                                ),
                            child: Text(
                              title.isEmpty ? ' ' : title,
                              key: ValueKey(title),
                              style: const TextStyle(
                                fontSize: 36,
                                fontWeight: FontWeight.w700,
                                letterSpacing: -1,
                                color: Colors.white,
                                height: 1.1,
                              ),
                            ),
                          ),
                          const SizedBox(height: 8),
                          AnimatedSwitcher(
                            duration: const Duration(milliseconds: 350),
                            child: Text(
                              subtitle.isEmpty ? ' ' : subtitle,
                              key: ValueKey(subtitle),
                              style: TextStyle(
                                fontSize: 16,
                                fontWeight: FontWeight.w400,
                                color: Colors.white.withValues(alpha: 0.72),
                                height: 1.4,
                              ),
                            ),
                          ),
                        ],
                      ),
                    ),
                  ],
                ),
              ),
            );
          },
        );
      },
    );
  }
}

// ---------------------------------------------------------------------------
// Tile artboard widget
// ---------------------------------------------------------------------------

class _TileArtboardWidget extends StatelessWidget {
  const _TileArtboardWidget(this.vm);
  final _DemoVmHandle vm;

  static const _kindIcons = <String, IconData>{
    'flight': Icons.flight_takeoff,
    'hotel': Icons.hotel,
    'place': Icons.place,
    'task': Icons.check_circle_outline,
  };

  static const _kindColors = <String, Color>{
    'flight': Color(0xFF00BCD4),
    'hotel': Color(0xFF7C4DFF),
    'place': Color(0xFF4CAF50),
    'task': Color(0xFF00897B),
  };

  @override
  Widget build(BuildContext context) {
    return ValueListenableBuilder<int>(
      valueListenable: vm.notifier,
      builder: (context, snapshot, child) {
        final kind = vm.state.strings['kind'] ?? 'place';
        final line1 = vm.state.strings['line1'] ?? '';
        final line2 = vm.state.strings['line2'] ?? '';
        final line3 = vm.state.strings['line3'] ?? '';
        final kindColor = _kindColors[kind] ?? const Color(0xFF90A4AE);
        final icon = _kindIcons[kind] ?? Icons.widgets_outlined;

        return ClipRRect(
          borderRadius: BorderRadius.circular(12),
          child: BackdropFilter(
            filter: ui.ImageFilter.blur(sigmaX: 10, sigmaY: 10),
            child: Container(
              height: 180,
              decoration: BoxDecoration(
                color: const Color(0xFF111827).withValues(alpha: 0.85),
                borderRadius: BorderRadius.circular(12),
                border: Border.all(
                  color: Colors.white.withValues(alpha: 0.08),
                ),
              ),
              child: Row(
                children: [
                  SizedBox(
                    width: 72,
                    child: Center(
                      child: Container(
                        width: 48,
                        height: 48,
                        decoration: BoxDecoration(
                          shape: BoxShape.circle,
                          gradient: RadialGradient(
                            colors: [
                              kindColor.withValues(alpha: 0.9),
                              kindColor.withValues(alpha: 0.3),
                            ],
                          ),
                        ),
                        child: Icon(icon, color: Colors.white, size: 22),
                      ),
                    ),
                  ),
                  Expanded(
                    child: Padding(
                      padding: const EdgeInsets.symmetric(
                          vertical: 20, horizontal: 8),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        mainAxisAlignment: MainAxisAlignment.center,
                        children: [
                          AnimatedSwitcher(
                            duration: const Duration(milliseconds: 350),
                            transitionBuilder: (child, anim) =>
                                FadeTransition(
                                  opacity: anim,
                                  child: SlideTransition(
                                    position: Tween<Offset>(
                                      begin: const Offset(0, 0.2),
                                      end: Offset.zero,
                                    ).animate(anim),
                                    child: child,
                                  ),
                                ),
                            child: Text(
                              line1.isEmpty ? '—' : line1,
                              key: ValueKey(line1),
                              style: const TextStyle(
                                fontSize: 16,
                                fontWeight: FontWeight.w600,
                                color: Colors.white,
                              ),
                            ),
                          ),
                          if (line2.isNotEmpty) ...[
                            const SizedBox(height: 4),
                            Text(
                              line2,
                              style: TextStyle(
                                fontSize: 13,
                                color: Colors.white.withValues(alpha: 0.68),
                              ),
                            ),
                          ],
                          if (line3.isNotEmpty) ...[
                            const SizedBox(height: 4),
                            Text(
                              line3,
                              style: TextStyle(
                                fontFamily: 'monospace',
                                fontSize: 12,
                                color: Colors.white.withValues(alpha: 0.55),
                              ),
                            ),
                          ],
                        ],
                      ),
                    ),
                  ),
                  const SizedBox(width: 12),
                ],
              ),
            ),
          ),
        );
      },
    );
  }
}

// ---------------------------------------------------------------------------
// Badge artboard widget — value0to1 flows through _TweenedNumber so the ring
// sweep honors any AnimSpec the script provided. Bare writes snap.
// ---------------------------------------------------------------------------

class _BadgeArtboardWidget extends StatefulWidget {
  const _BadgeArtboardWidget(this.vm);
  final _DemoVmHandle vm;

  @override
  State<_BadgeArtboardWidget> createState() => _BadgeArtboardWidgetState();
}

class _BadgeArtboardWidgetState extends State<_BadgeArtboardWidget>
    with SingleTickerProviderStateMixin {
  late final _TweenedNumber _ringValue;

  @override
  void initState() {
    super.initState();
    _ringValue = _TweenedNumber(
      vm: widget.vm,
      field: 'value0to1',
      vsync: this,
    );
  }

  @override
  void dispose() {
    _ringValue.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return ValueListenableBuilder<int>(
      valueListenable: widget.vm.notifier,
      builder: (context, snapshot, child) {
        final label = widget.vm.state.strings['label'] ?? '';

        return Container(
          height: 120,
          decoration: BoxDecoration(
            color: const Color(0xFF111827).withValues(alpha: 0.9),
            borderRadius: BorderRadius.circular(12),
            border: Border.all(
              color: Colors.white.withValues(alpha: 0.08),
            ),
          ),
          child: Row(
            children: [
              const SizedBox(width: 20),
              AnimatedBuilder(
                animation: _ringValue,
                builder: (context, _) => CustomPaint(
                  size: const Size(72, 72),
                  painter: _RingPainter(_ringValue.value),
                ),
              ),
              const SizedBox(width: 20),
              Column(
                mainAxisAlignment: MainAxisAlignment.center,
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    label.toUpperCase(),
                    style: TextStyle(
                      fontSize: 13,
                      letterSpacing: 1.5,
                      color: Colors.white.withValues(alpha: 0.6),
                      fontWeight: FontWeight.w500,
                    ),
                  ),
                  const SizedBox(height: 4),
                  AnimatedBuilder(
                    animation: _ringValue,
                    builder: (context, _) {
                      final pct = (_ringValue.value * 100).round();
                      return AnimatedSwitcher(
                        duration: const Duration(milliseconds: 200),
                        child: Text(
                          '$pct%',
                          key: ValueKey(pct),
                          style: const TextStyle(
                            fontSize: 28,
                            fontWeight: FontWeight.w700,
                            color: Colors.white,
                          ),
                        ),
                      );
                    },
                  ),
                ],
              ),
            ],
          ),
        );
      },
    );
  }
}

class _RingPainter extends CustomPainter {
  const _RingPainter(this.value);
  final double value;

  @override
  void paint(Canvas canvas, Size size) {
    final center = Offset(size.width / 2, size.height / 2);
    final radius = size.width / 2 - 4;
    final rect = Rect.fromCircle(center: center, radius: radius);

    canvas.drawCircle(
      center,
      radius,
      Paint()
        ..color = Colors.white.withValues(alpha: 0.1)
        ..style = PaintingStyle.stroke
        ..strokeWidth = 4,
    );

    canvas.drawArc(
      rect,
      -math.pi / 2,
      2 * math.pi * value.clamp(0.0, 1.0),
      false,
      Paint()
        ..shader = SweepGradient(
          colors: const [Color(0xFF00BCD4), Color(0xFFE040FB)],
          startAngle: -math.pi / 2,
          endAngle: 3 * math.pi / 2,
        ).createShader(rect)
        ..style = PaintingStyle.stroke
        ..strokeWidth = 4
        ..strokeCap = StrokeCap.round,
    );
  }

  @override
  bool shouldRepaint(_RingPainter old) => old.value != value;
}

// ---------------------------------------------------------------------------
// PersonaInline artboard widget — energy flows through _TweenedNumber so glow
// radius interpolates smoothly when AnimSpec is set, snaps otherwise.
// ---------------------------------------------------------------------------

class _PersonaInlineArtboardWidget extends StatefulWidget {
  const _PersonaInlineArtboardWidget(this.vm);
  final _DemoVmHandle vm;

  @override
  State<_PersonaInlineArtboardWidget> createState() =>
      _PersonaInlineArtboardWidgetState();
}

class _PersonaInlineArtboardWidgetState
    extends State<_PersonaInlineArtboardWidget>
    with TickerProviderStateMixin {
  late final AnimationController _pulse;
  late final _TweenedNumber _energy;
  late Animation<double> _scale;
  late Animation<double> _opacity;

  @override
  void initState() {
    super.initState();
    _pulse = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 600),
    );
    _scale = ConstantTween<double>(1.0).animate(_pulse);
    _opacity = ConstantTween<double>(1.0).animate(_pulse);
    _energy = _TweenedNumber(vm: widget.vm, field: 'energy', vsync: this);
    // Register trigger — when RiveArtboard._wireTriggers calls vm.onTrigger,
    // it lands here and drives the scale+opacity flash.
    widget.vm.onTrigger('pulse', _onPulseTrigger);
  }

  void _onPulseTrigger() {
    _scale = TweenSequence<double>([
      TweenSequenceItem(tween: Tween(begin: 1.0, end: 1.3), weight: 40),
      TweenSequenceItem(tween: Tween(begin: 1.3, end: 1.0), weight: 60),
    ]).animate(CurvedAnimation(parent: _pulse, curve: Curves.easeInOut));
    _opacity = TweenSequence<double>([
      TweenSequenceItem(tween: Tween(begin: 1.0, end: 0.5), weight: 40),
      TweenSequenceItem(tween: Tween(begin: 0.5, end: 1.0), weight: 60),
    ]).animate(CurvedAnimation(parent: _pulse, curve: Curves.easeInOut));
    _pulse
      ..reset()
      ..forward();
  }

  @override
  void dispose() {
    _energy.dispose();
    _pulse.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return ValueListenableBuilder<int>(
      valueListenable: widget.vm.notifier,
      builder: (context, snapshot, child) {
        final mood = widget.vm.state.strings['mood'] ?? 'centered';
        final moodCol = _moodColor(mood);

        return SizedBox(
          height: 80,
          child: Center(
            child: AnimatedBuilder(
              animation: Listenable.merge([_pulse, _energy]),
              builder: (context, _) {
                final glowRadius = (_energy.value * 28).clamp(0.0, 28.0);
                return ScaleTransition(
                  scale: _scale,
                  child: FadeTransition(
                    opacity: _opacity,
                    child: SizedBox(
                      width: 64,
                      height: 64,
                      child: CustomPaint(
                        painter: _PersonaPainter(
                          moodColor: moodCol,
                          glowRadius: glowRadius,
                        ),
                      ),
                    ),
                  ),
                );
              },
            ),
          ),
        );
      },
    );
  }
}

class _PersonaPainter extends CustomPainter {
  const _PersonaPainter({
    required this.moodColor,
    required this.glowRadius,
  });

  final Color moodColor;
  final double glowRadius;

  @override
  void paint(Canvas canvas, Size size) {
    final center = Offset(size.width / 2, size.height / 2);
    final outerRadius = size.width / 2 - 2;

    if (glowRadius > 0) {
      canvas.drawCircle(
        center,
        glowRadius,
        Paint()
          ..color = moodColor.withValues(alpha: 0.35)
          ..maskFilter =
              MaskFilter.blur(BlurStyle.normal, glowRadius / 2),
      );
    }

    canvas.drawCircle(
      center,
      outerRadius - 6,
      Paint()
        ..shader = RadialGradient(
          colors: [
            moodColor.withValues(alpha: 0.8),
            moodColor.withValues(alpha: 0.1),
          ],
        ).createShader(Rect.fromCircle(center: center, radius: outerRadius)),
    );

    canvas.drawCircle(
      center,
      outerRadius,
      Paint()
        ..color = moodColor
        ..style = PaintingStyle.stroke
        ..strokeWidth = 2,
    );
  }

  @override
  bool shouldRepaint(_PersonaPainter old) =>
      old.moodColor != moodColor || old.glowRadius != glowRadius;
}

// ---------------------------------------------------------------------------
// Spacer artboard widget — height flows through _TweenedNumber so a script
// can grow it in over a curve when introducing a new motif.
// ---------------------------------------------------------------------------

class _SpacerArtboardWidget extends StatefulWidget {
  const _SpacerArtboardWidget(this.vm);
  final _DemoVmHandle vm;

  @override
  State<_SpacerArtboardWidget> createState() => _SpacerArtboardWidgetState();
}

class _SpacerArtboardWidgetState extends State<_SpacerArtboardWidget>
    with TickerProviderStateMixin {
  late final AnimationController _anim;
  late final _TweenedNumber _height;

  @override
  void initState() {
    super.initState();
    _anim = AnimationController(
      vsync: this,
      duration: const Duration(seconds: 3),
    )..repeat();
    _height = _TweenedNumber(vm: widget.vm, field: 'height', vsync: this);
  }

  @override
  void dispose() {
    _anim.dispose();
    _height.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return ValueListenableBuilder<int>(
      valueListenable: widget.vm.notifier,
      builder: (context, snapshot, child) {
        final motif = widget.vm.state.strings['motif'] ?? '';

        return AnimatedBuilder(
          animation: Listenable.merge([_anim, _height]),
          builder: (context, _) {
            final h = _height.value;
            if (motif == 'rain') {
              return SizedBox(
                height: h,
                width: double.infinity,
                child: CustomPaint(painter: _RainPainter(_anim.value)),
              );
            }
            if (motif == 'wave') {
              return SizedBox(
                height: h,
                width: double.infinity,
                child: CustomPaint(painter: _WavePainter(_anim.value)),
              );
            }
            return SizedBox(height: h);
          },
        );
      },
    );
  }
}

class _RainPainter extends CustomPainter {
  const _RainPainter(this.t);
  final double t;

  static const _streaks = 6;

  @override
  void paint(Canvas canvas, Size size) {
    final paint = Paint()
      ..color = Colors.white.withValues(alpha: 0.18)
      ..strokeWidth = 1
      ..strokeCap = StrokeCap.round;

    for (var i = 0; i < _streaks; i++) {
      final xFrac = (i + 1) / (_streaks + 1);
      final x = xFrac * size.width;
      final yOffset = ((t + i * 0.15) % 1.0) * (size.height + 12) - 6;
      canvas.drawLine(Offset(x, yOffset), Offset(x, yOffset + 6), paint);
    }
  }

  @override
  bool shouldRepaint(_RainPainter old) => old.t != t;
}

class _WavePainter extends CustomPainter {
  const _WavePainter(this.t);
  final double t;

  @override
  void paint(Canvas canvas, Size size) {
    final path = Path()..moveTo(0, size.height);
    for (var x = 0.0; x <= size.width; x++) {
      final y = size.height / 2 +
          math.sin((x / size.width * 2 * math.pi) + t * 2 * math.pi) *
              (size.height * 0.3);
      path.lineTo(x, y);
    }
    path
      ..lineTo(size.width, size.height)
      ..close();
    canvas.drawPath(
      path,
      Paint()
        ..color = Colors.white.withValues(alpha: 0.08)
        ..style = PaintingStyle.fill,
    );
  }

  @override
  bool shouldRepaint(_WavePainter old) => old.t != t;
}

// ---------------------------------------------------------------------------
// Fallback widget for unknown artboard names
// ---------------------------------------------------------------------------

class _UnknownArtboardWidget extends StatelessWidget {
  const _UnknownArtboardWidget(this.vm);
  final _DemoVmHandle vm;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(8),
      decoration: BoxDecoration(
        color: Colors.red.withValues(alpha: 0.15),
        borderRadius: BorderRadius.circular(6),
      ),
      child: const Text(
        'unknown demo artboard',
        style: TextStyle(color: Colors.redAccent, fontSize: 12),
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Noise painter — animated gradient shimmer behind Hero text
// ---------------------------------------------------------------------------

class _NoisePainter extends CustomPainter {
  const _NoisePainter(this.t, this.color);
  final double t;
  final Color color;

  @override
  void paint(Canvas canvas, Size size) {
    final phase = t * 2 * math.pi;
    final cx = size.width * (0.5 + 0.3 * math.cos(phase));
    final cy = size.height * (0.5 + 0.3 * math.sin(phase));
    canvas.drawCircle(
      Offset(cx, cy),
      size.width * 0.6,
      Paint()
        ..shader = RadialGradient(
          colors: [color.withValues(alpha: 0.45), Colors.transparent],
        ).createShader(Rect.fromLTWH(0, 0, size.width, size.height))
        ..maskFilter = const MaskFilter.blur(BlurStyle.normal, 32),
    );
  }

  @override
  bool shouldRepaint(_NoisePainter old) => old.t != t || old.color != color;
}
