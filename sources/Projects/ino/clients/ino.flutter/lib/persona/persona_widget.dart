import 'dart:math';

import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:ino_flutter/persona/persona_state.dart';
import 'package:ino_flutter/state/persona_bloc.dart';
import 'package:ino_flutter/state/timeline_bloc.dart';
import 'package:rive/rive.dart' as rive;

const String _defaultRiveAsset = 'assets/rive/emoji.riv';

class PersonaWidget extends StatefulWidget {
  const PersonaWidget({super.key, this.size = 200});

  final double size;

  @override
  State<PersonaWidget> createState() => _PersonaWidgetState();
}

class _PersonaWidgetState extends State<PersonaWidget>
    with TickerProviderStateMixin {
  late final AnimationController _renderLoop;
  late final AnimationController _pulseController;
  late final Animation<double> _pulseDecay;
  double _currentPulse = 0.0;
  int _lastEventCount = 0;

  @override
  void initState() {
    super.initState();
    // smooth render loop -- drives the morphing shape and orbiting dots
    _renderLoop = AnimationController(
      vsync: this,
      duration: const Duration(seconds: 3),
    )..repeat();

    // pulse decay: fires on each signal, decays over 1.5s
    _pulseController = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 1500),
    );
    _pulseDecay = CurvedAnimation(
      parent: _pulseController,
      curve: Curves.easeOutExpo,
    );
    _pulseDecay.addListener(() {
      _currentPulse = 1.0 - _pulseDecay.value;
    });
  }

  void _onSignalReceived() {
    _pulseController.forward(from: 0.0);
  }

  @override
  void dispose() {
    _renderLoop.dispose();
    _pulseController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return BlocListener<TimelineBloc, TimelineBlocState>(
      listenWhen: (prev, curr) =>
          curr.isLive && curr.events.length > prev.events.length,
      listener: (context, timelineState) {
        final newCount = timelineState.events.length;
        if (newCount > _lastEventCount) {
          _onSignalReceived();
          _lastEventCount = newCount;
        }
      },
      child: BlocBuilder<PersonaBloc, PersonaStateModel>(
        builder: (context, persona) {
          // adapt render loop speed to energy level
          final loopDuration = _durationForEnergy(persona.energy);
          if (_renderLoop.duration != loopDuration) {
            _renderLoop.duration = loopDuration;
          }

          Widget customPaintOrb() => AnimatedBuilder(
                animation: Listenable.merge([_renderLoop, _pulseDecay]),
                builder: (context, child) {
                  return CustomPaint(
                    size: Size(widget.size, widget.size),
                    painter: _PersonaPainter(
                      emotion: persona.emotion,
                      energy: persona.energy,
                      neuronCount: persona.neuronCount,
                      synapseRate: persona.synapseRate,
                      animationValue: _renderLoop.value,
                      signalPulse: _currentPulse,
                      activeSkillCount: persona.activeSkillCount,
                    ),
                  );
                },
              );

          return Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              // Rive primary (defaults to the bundled persona_orb.riv when the
              // server hasn't pushed a persona-specific URL), CustomPaint kept
              // as a hidden fallback for missing / failed loads.
              _PersonaOrb(
                assetUrl: persona.riveAssetUrl ?? _defaultRiveAsset,
                size: widget.size,
                fallback: customPaintOrb,
              ),
              const SizedBox(height: 8),
              _StatusLine(persona: persona),
            ],
          );
        },
      ),
    );
  }

  Duration _durationForEnergy(double energy) {
    // high energy = faster loop (1.5s), low energy = slow drift (5s)
    final seconds = 5.0 - (energy * 3.5);
    return Duration(milliseconds: (seconds * 1000).round());
  }
}

class _StatusLine extends StatelessWidget {
  const _StatusLine({required this.persona});

  final PersonaStateModel persona;

  @override
  Widget build(BuildContext context) {
    final color = _colorForEmotion(persona.emotion);
    if (persona.currentAction != null) {
      return Text(
        persona.currentAction!,
        style: TextStyle(fontSize: 11, color: color.withValues(alpha: 0.7)),
        overflow: TextOverflow.ellipsis,
        maxLines: 1,
      );
    }
    final skillLabel = persona.activeSkillCount == 1
        ? '1 skill active'
        : '${persona.activeSkillCount} skills active';
    return Text(
      '${persona.personaName} \u00b7 $skillLabel',
      style: TextStyle(fontSize: 11, color: Colors.grey.shade500),
    );
  }
}

// Renders a Rive artboard when a .riv asset is available, and falls back to
// the CustomPaint orb otherwise. The fallback is also used if Rive fails to
// load the asset at runtime (empty file, missing file, parse error). The
// CustomPaint version is visually rich on its own, so the demo ships with
// or without a real Rive file.
class _PersonaOrb extends StatefulWidget {
  const _PersonaOrb({
    required this.size,
    required this.fallback,
    this.assetUrl,
  });

  final double size;
  final Widget Function() fallback;
  final String? assetUrl;

  @override
  State<_PersonaOrb> createState() => _PersonaOrbState();
}

class _PersonaOrbState extends State<_PersonaOrb> {
  rive.File? _file;
  rive.RiveWidgetController? _controller;
  bool _loadFailed = false;

  @override
  void initState() {
    super.initState();
    _loadIfNeeded();
  }

  @override
  void didUpdateWidget(covariant _PersonaOrb oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.assetUrl != widget.assetUrl) {
      _disposeRive();
      _loadFailed = false;
      _loadIfNeeded();
    }
  }

  Future<void> _loadIfNeeded() async {
    final asset = widget.assetUrl;
    if (asset == null || asset.isEmpty) return;
    try {
      final file = await rive.File.asset(asset, riveFactory: rive.Factory.rive);
      if (!mounted || file == null) {
        file?.dispose();
        if (mounted) setState(() => _loadFailed = true);
        if (file == null) {
          debugPrint('[PersonaOrb] Rive asset returned null: $asset');
        }
        return;
      }
      final controller = rive.RiveWidgetController(file);
      setState(() {
        _file = file;
        _controller = controller;
      });
    } catch (e, st) {
      debugPrint('[PersonaOrb] Rive load failed for $asset: $e\n$st');
      if (mounted) setState(() => _loadFailed = true);
    }
  }

  void _disposeRive() {
    _controller?.dispose();
    _file?.dispose();
    _controller = null;
    _file = null;
  }

  @override
  void dispose() {
    _disposeRive();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final controller = _controller;
    if (controller == null || _loadFailed) {
      return widget.fallback();
    }
    return SizedBox(
      width: widget.size,
      height: widget.size,
      child: rive.RiveWidget(controller: controller),
    );
  }
}

Color _colorForEmotion(PersonaEmotion emotion) {
  return switch (emotion) {
    PersonaEmotion.sleeping => const Color(0xFF3D3D6B),
    PersonaEmotion.waking => const Color(0xFF5B5BA0),
    PersonaEmotion.idle => const Color(0xFF6C63FF),
    PersonaEmotion.listening => const Color(0xFF7B8CFF),
    PersonaEmotion.thinking => const Color(0xFFFF9F43),
    PersonaEmotion.acting => const Color(0xFF00D2FF),
    PersonaEmotion.responding => const Color(0xFF6C63FF),
    PersonaEmotion.celebrating => const Color(0xFF2ECC71),
    PersonaEmotion.confused => const Color(0xFFE74C3C),
    PersonaEmotion.evolving => const Color(0xFFA855F7),
    PersonaEmotion.searching => const Color(0xFF00B4D8),
    PersonaEmotion.presenting => const Color(0xFF2ECC71),
  };
}

class _PersonaPainter extends CustomPainter {
  _PersonaPainter({
    required this.emotion,
    required this.energy,
    required this.neuronCount,
    required this.synapseRate,
    required this.animationValue,
    required this.signalPulse,
    required this.activeSkillCount,
  });

  final PersonaEmotion emotion;
  final double energy;
  final int neuronCount;
  final double synapseRate;
  final double animationValue;
  final double signalPulse;
  final int activeSkillCount;

  @override
  void paint(Canvas canvas, Size size) {
    final center = Offset(size.width / 2, size.height / 2);
    final baseRadius = size.width * 0.3;
    final color = _colorForEmotion(emotion);
    final morphAmount = energy * 0.15;
    final phase = animationValue * 2 * pi;

    // idle heartbeat: subtle sine wave on ring size (~2% oscillation)
    final heartbeat = sin(phase * 0.5) * 0.02;

    final isPresenting = emotion == PersonaEmotion.presenting;
    final drawCenter = isPresenting
        ? Offset(center.dx, center.dy - baseRadius * 0.3)
        : center;

    // searching state: expanding radar pulse rings
    if (emotion == PersonaEmotion.searching) {
      for (var ring = 0; ring < 3; ring++) {
        final ringRadius =
            baseRadius * (1.2 + ring * 0.3 + animationValue * 0.5);
        final ringOpacity =
            (1.0 - (animationValue + ring * 0.3).clamp(0.0, 1.0)) * 0.3;
        canvas.drawCircle(
          drawCenter,
          ringRadius,
          Paint()
            ..color = color.withValues(alpha: ringOpacity)
            ..style = PaintingStyle.stroke
            ..strokeWidth = 1.5,
        );
      }
    }

    // glow circle -- intensity modulated by signal pulse
    final glowRadius = baseRadius * (1.3 + heartbeat + signalPulse * 0.15);
    final glowAlpha = (40 + (signalPulse * 80)).round().clamp(0, 255);
    final glowBlur = baseRadius * (0.6 + signalPulse * 0.3);
    final glowPaint = Paint()
      ..color = color.withAlpha(glowAlpha)
      ..maskFilter = MaskFilter.blur(BlurStyle.normal, glowBlur);
    canvas.drawCircle(drawCenter, glowRadius, glowPaint);

    // morphing shape via path with sine-wave deformation
    final path = Path();
    const segments = 72;
    for (var i = 0; i <= segments; i++) {
      final angle = (i / segments) * 2 * pi;
      final deformation = 1.0 +
          morphAmount * sin(3 * angle + phase) +
          morphAmount * 0.5 * sin(5 * angle - phase * 1.3);
      var r = baseRadius * deformation * (1.0 + heartbeat);

      if (isPresenting && sin(angle) > 0) {
        r *= (1.0 - 0.3 * sin(angle));
      }

      final x = drawCenter.dx + r * cos(angle);
      final y = drawCenter.dy + r * sin(angle);

      if (i == 0) {
        path.moveTo(x, y);
      } else {
        path.lineTo(x, y);
      }
    }
    path.close();

    final shapePaint = Paint()
      ..color = color
      ..style = PaintingStyle.fill;
    canvas.drawPath(path, shapePaint);

    // inner highlight
    final highlightPaint = Paint()
      ..color = color.withAlpha(80)
      ..style = PaintingStyle.fill;
    canvas.drawCircle(
      drawCenter + Offset(0, -baseRadius * 0.15),
      baseRadius * 0.5,
      highlightPaint,
    );

    // orbiting dots -- count driven by activeSkillCount (fallback to neuronCount)
    final dotCount = activeSkillCount > 0 ? activeSkillCount : neuronCount;
    if (dotCount > 0) {
      final orbitRadius = baseRadius * 1.5;
      // pulse makes dots move faster
      final speedMultiplier = 1.0 + signalPulse * 2.0;
      final dotPhase = phase * speedMultiplier;
      final dotPaint = Paint()..color = color.withValues(alpha: 0.6);
      for (var i = 0; i < dotCount; i++) {
        final angle = (2 * pi * i / dotCount) + dotPhase;
        final dotCenter = Offset(
          drawCenter.dx + orbitRadius * cos(angle),
          drawCenter.dy + orbitRadius * sin(angle),
        );
        canvas.drawCircle(dotCenter, 3, dotPaint);

        final trailAngle = angle - 0.3;
        canvas.drawCircle(
          Offset(
            drawCenter.dx + orbitRadius * cos(trailAngle),
            drawCenter.dy + orbitRadius * sin(trailAngle),
          ),
          2,
          Paint()..color = color.withValues(alpha: 0.2),
        );
      }
    }

    // synapse glow ring -- signal-pulse-driven intensity
    final ringAlpha = (0.3 + signalPulse * 0.5).clamp(0.0, 0.8);
    final ringWidth = 1.5 + signalPulse * 3.0 +
        (synapseRate > 0 ? (synapseRate / 5.0).clamp(0.0, 2.0) : 0.0);
    final ringRadius = baseRadius * (1.15 + heartbeat + signalPulse * 0.08);
    canvas.drawCircle(
      drawCenter,
      ringRadius,
      Paint()
        ..color = color.withValues(alpha: ringAlpha)
        ..style = PaintingStyle.stroke
        ..strokeWidth = ringWidth,
    );
  }

  @override
  bool shouldRepaint(_PersonaPainter oldDelegate) =>
      oldDelegate.emotion != emotion ||
      oldDelegate.energy != energy ||
      oldDelegate.neuronCount != neuronCount ||
      oldDelegate.synapseRate != synapseRate ||
      oldDelegate.animationValue != animationValue ||
      oldDelegate.signalPulse != signalPulse ||
      oldDelegate.activeSkillCount != activeSkillCount;
}
