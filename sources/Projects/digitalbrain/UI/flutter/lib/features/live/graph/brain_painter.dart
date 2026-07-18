import 'dart:math';

import 'package:digitalbrain_flutter/digital_brain_ui/digital_brain_ui.dart';
import 'package:digitalbrain_flutter/theme/digitalbrain_theme.dart';
import 'package:digitalbrain_flutter/widgets/neuron_vector_logo.dart';
import 'package:flutter/material.dart';
import 'package:vector_math/vector_math_64.dart' as vm;

import 'cluster_layout.dart';
import 'comet.dart';
import 'domain_palette.dart';class BrainPainter extends CustomPainter {
  BrainPainter({
    required this.nodes,
    required this.edges,
    required this.comets,
    required this.cam,
    required this.zoom,
    required this.selectedNeuronId,
    required this.selectedEdge,
    required this.now,
    this.liveMode = true,
    this.highlightEdge,
    this.activeCorrelations = const {},
    this.collapseWaves = const [],
    this.birthPulses = const [],
    this.rimGlowEnabled = true,
    this.showSynapses = false,
    this.replaceSpheresWithIcons = false,
    this.activeLayout = 'default',
    this.activeTab = 0,
    this.isMonochrome = false,
  });

  final List<GraphNode> nodes;
  final List<GraphEdge> edges;
  final List<Comet> comets;
  final vm.Matrix4 cam;
  final double zoom;
  final String? selectedNeuronId;
  final GraphEdge? selectedEdge;
  final DateTime now;
  final bool liveMode;
  final GraphEdge? highlightEdge;
  final Set<String> activeCorrelations;
  final List<CollapseWave> collapseWaves;
  final List<BirthPulse> birthPulses;
  final bool rimGlowEnabled;
  final bool showSynapses;
  final bool replaceSpheresWithIcons;
  final String activeLayout;
  final int activeTab;
  final bool isMonochrome;

  static const _focal = 700.0;

  @override
  void paint(Canvas canvas, Size size) {
    _drawBackground(canvas, size);
    _drawClusterHalos(canvas, size);

    final projection = Projection(cam: cam, zoom: zoom, size: size);
    final nodesById = {for (final n in nodes) n.id: n};
    final projected = {for (final n in nodes) n.id: _project(n.position, size)};

    // Static "stale" filaments — faint fall-through for edges no longer animated.
    _drawStaticEdges(canvas, projected);

    // Active comets on top — only while live mode is on.
    if (liveMode) {
      drawComets(
        canvas: canvas,
        comets: comets,
        nodesById: nodesById,
        projection: projection,
        now: now,
      );
    }

    if (highlightEdge != null) {
      _drawHighlightedEdge(canvas, projected, highlightEdge!);
    }

    _drawNodes(canvas, projected);
    if (activeCorrelations.isNotEmpty) {
      _drawCorrelationRimGlow(canvas, projected);
    }
    _drawCollapseWaves(canvas, size);
    _drawBirthPulses(canvas);
    _drawLayoutOverlay(canvas, size, projected);
  }

  void _drawBirthPulses(Canvas canvas) {
    if (birthPulses.isEmpty) return;
    final now = DateTime.now();
    for (final pulse in birthPulses) {
      final t = pulse.progress(now);
      if (t >= 1.0) continue;

      final mid = Offset.lerp(pulse.origin, pulse.target, t)!;
      final arcPaint = Paint()
        ..color = Colors.white.withValues(alpha: 0.55 * (1 - t))
        ..strokeWidth = 2.0
        ..maskFilter = const MaskFilter.blur(BlurStyle.normal, 3);
      canvas.drawLine(pulse.origin, mid, arcPaint);

      if (t > 0.85) {
        final rt = (t - 0.85) / 0.15;
        final ringPaint = Paint()
          ..style = PaintingStyle.stroke
          ..strokeWidth = 1.5
          ..color = Colors.white.withValues(alpha: 0.6 * (1 - rt));
        canvas.drawCircle(pulse.target, 22 * rt, ringPaint);
      }
    }
  }

  void _drawCollapseWaves(Canvas canvas, Size size) {
    if (collapseWaves.isEmpty) return;
    final now = DateTime.now();
    for (final wave in collapseWaves) {
      final t = wave.progress(now);
      if (t >= 1.0) continue;
      final maxR = sqrt(size.width * size.width + size.height * size.height);
      final r = t * maxR;
      final ring = Paint()
        ..style = PaintingStyle.stroke
        ..strokeWidth = 1.5 + 6 * (1 - t)
        ..color = Colors.white.withValues(alpha: 0.30 * (1 - t));
      canvas.drawCircle(wave.origin, r, ring);
    }
  }

  void _drawHighlightedEdge(
    Canvas canvas,
    Map<String, _Projected> projected,
    GraphEdge edge,
  ) {
    final a = projected[edge.fromId];
    final b = projected[edge.toId];
    if (a == null || b == null) return;
    const gold = Color(0xFFFFD166);
    final linePaint = Paint()
      ..color = gold
      ..strokeWidth = 3.0
      ..strokeCap = StrokeCap.round;
    canvas.drawLine(a.screen, b.screen, linePaint);
    final ringPaint = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 2.0
      ..color = gold;
    canvas.drawCircle(a.screen, 16, ringPaint);
    canvas.drawCircle(b.screen, 16, ringPaint);
  }

  void _drawBackground(Canvas canvas, Size size) {
    final paint = Paint()
      ..shader = const RadialGradient(
        colors: [Color(0xFF1a1230), Color(0xFF0a0617)],
      ).createShader(Offset.zero & size);
    canvas.drawRect(Offset.zero & size, paint);
  }

  void _drawClusterHalos(Canvas canvas, Size size) {
    for (final entry in domainPalette.entries) {
      final style = entry.value;
      final p = _project(style.anchor, size);
      final radius = 60.0 * (zoom * _depthScale(p.depth));
      final color = style.color.withValues(alpha: 0.10);
      canvas.drawCircle(
        p.screen,
        radius,
        Paint()
          ..color = color
          ..blendMode = BlendMode.plus,
      );
    }
  }

  void _drawStaticEdges(Canvas canvas, Map<String, _Projected> projected) {
    if (!showSynapses) return;
    // Filaments under the comets. Recent edges glow brighter so freshly-fired
    // synapses remain visible after the comet linger has decayed. Fades completely 
    // to 0.0 over 12s, keeping only active task paths and recent synapse trails visible.
    for (final e in edges) {
      final a = projected[e.fromId];
      final b = projected[e.toId];
      if (a == null || b == null) continue;
      final isSelected = identical(e, selectedEdge);
      final isPartOfActiveCorrelation = activeCorrelations.contains(e.correlationId);
      final family = colorForSynapseType(e.typeName);
      final ageSeconds = now.difference(e.at).inMilliseconds / 1000.0;

      // Fading synapse trail of 12 seconds
      final recency = (1.0 - (ageSeconds / 12.0)).clamp(0.0, 1.0);

      // Avoid drawing stale background filaments entirely
      if (recency <= 0.0 && !isSelected && !isPartOfActiveCorrelation) {
        continue;
      }

      final alpha = isSelected 
          ? 0.65 
          : (isPartOfActiveCorrelation ? 0.45 + 0.35 * recency : 0.65 * recency);
      final width = isSelected ? 2.4 : (1.2 + 1.4 * recency);
      final color = (isSelected ? const Color(0xFFFFD166) : family).withValues(
        alpha: alpha,
      );

      // Soft outer glow under bright/recent edges so they read against the
      // dark background.
      if (recency > 0.05 || isSelected || isPartOfActiveCorrelation) {
        final glow = Paint()
          ..color = color.withValues(alpha: alpha * 0.35)
          ..strokeWidth = width + 3.0
          ..strokeCap = StrokeCap.round
          ..maskFilter = const MaskFilter.blur(BlurStyle.normal, 3.0);
        canvas.drawLine(a.screen, b.screen, glow);
      }

      final paint = Paint()
        ..color = color
        ..strokeWidth = width
        ..strokeCap = StrokeCap.round;
      canvas.drawLine(a.screen, b.screen, paint);
    }
  }

  void _drawNodes(Canvas canvas, Map<String, _Projected> projected) {
    final byId = {for (final n in nodes) n.id: n};
    final sorted = projected.entries.toList()
      ..sort((x, y) => y.value.depth.compareTo(x.value.depth));
    
    final double pulseValue = (DateTime.now().millisecondsSinceEpoch % 5000) / 5000.0;
    final double pulseFactor = 1.0 + 0.08 * sin(pulseValue * 2.0 * pi * 2.0);

    for (final entry in sorted) {
      final p = entry.value;
      final n = byId[entry.key];
      final isSelected = entry.key == selectedNeuronId;
      final bool isActive = n?.isActive ?? false;

      final radius =
          (isSelected ? 14.0 : (isActive ? 12.0 : 10.0)) * (1.0 + 0.5 / max(0.1, p.depth / _focal));
      
      final base = isSelected
          ? const Color(0xFFFFD166)
          : (isActive ? const Color(0xFFFF9F1C) : styleForDomain(n?.domain).color);

      final strokeColor = isSelected
          ? const Color(0xFFFFD166)
          : Color.lerp(base, Colors.white, 0.35)!;

      final adaptedColor = _adaptColor(strokeColor);
      final adaptedBase = _adaptColor(base);

      // Active state pulsating volumetric glow (pulsates at breathing 1200ms rate)
      if (isActive) {
        final double activePulse = 1.0 + 0.12 * sin((DateTime.now().millisecondsSinceEpoch % 1200) / 1200.0 * 2.0 * pi);
        canvas.drawCircle(
          p.screen,
          radius * 1.6 * activePulse,
          Paint()
            ..color = const Color(0xFFFF9F1C).withValues(alpha: 0.16)
            ..maskFilter = const MaskFilter.blur(BlurStyle.normal, 5.0),
        );
      }

      // Handle Tab 2: Neo-System Grid
      if (activeTab == 1) {
        final outlinePaint = Paint()
          ..color = adaptedColor
          ..strokeWidth = 1.2
          ..style = PaintingStyle.stroke;
        
        canvas.drawCircle(p.screen, radius * pulseFactor, outlinePaint);
        canvas.drawCircle(p.screen, 2.0, Paint()..color = adaptedColor);
        canvas.drawLine(Offset(p.screen.dx - radius * 1.3, p.screen.dy), Offset(p.screen.dx - radius * 0.9, p.screen.dy), outlinePaint);
        canvas.drawLine(Offset(p.screen.dx + radius * 0.9, p.screen.dy), Offset(p.screen.dx + radius * 1.3, p.screen.dy), outlinePaint);
        canvas.drawLine(Offset(p.screen.dx, p.screen.dy - radius * 1.3), Offset(p.screen.dx, p.screen.dy - radius * 0.9), outlinePaint);
        canvas.drawLine(Offset(p.screen.dx, p.screen.dy + radius * 0.9), Offset(p.screen.dx, p.screen.dy + radius * 1.3), outlinePaint);
        continue;
      }

      // Handle Tab 3: Cinematic Editorial (Extreme depth-of-field)
      if (activeTab == 2) {
        final double dofDist = (p.depth).abs();
        final double cinematicBlur = (dofDist / 70.0).clamp(0.0, 16.0);
        final double finalOpacity = (1.0 - (dofDist / 600.0)).clamp(0.05, 1.0);
        final double finalScale = (1.0 - (dofDist / 1200.0)).clamp(0.3, 1.2);

        final cPaint = Paint()
          ..color = adaptedColor.withValues(alpha: finalOpacity)
          ..style = PaintingStyle.fill;
        if (cinematicBlur > 1.0) cPaint.maskFilter = MaskFilter.blur(BlurStyle.normal, cinematicBlur);
        canvas.drawCircle(p.screen, radius * finalScale * pulseFactor, cPaint);
        continue;
      }

      // Handle Tab 6: Holographic Shield (Bubble Shell with Core Logo)
      if (activeTab == 5) {
        final ringPaint = Paint()
          ..color = adaptedColor.withValues(alpha: 0.3)
          ..strokeWidth = 1.0
          ..style = PaintingStyle.stroke;
        canvas.drawCircle(p.screen, radius * pulseFactor, ringPaint);

        final dashPaint = Paint()
          ..color = adaptedColor.withValues(alpha: 0.55)
          ..strokeWidth = 1.2
          ..style = PaintingStyle.stroke;
        for (int i = 0; i < 8; i++) {
          double startAngle = (i * 2.0 * pi / 8.0) + (pulseValue * 2.0 * pi * 0.18);
          canvas.drawArc(Rect.fromCircle(center: p.screen, radius: radius * 1.25), startAngle, 0.35, false, dashPaint);
        }

        canvas.drawCircle(p.screen, radius * 0.65, Paint()..color = adaptedColor.withValues(alpha: 0.12)..style = PaintingStyle.fill);

        _drawBillboardedIcon(
          canvas,
          p.screen,
          _getBrainIcon(entry.key),
          radius * 0.95 * pulseFactor,
          isMonochrome ? Colors.white : adaptedColor.withValues(alpha: 0.95),
        );
        continue;
      }

      // Handle Tab 7: Quantum Vector Matrix (Local electron orbital paths + Core Logo)
      if (activeTab == 6) {
        final ringPaint = Paint()
          ..color = adaptedColor.withValues(alpha: 0.28)
          ..strokeWidth = 0.8
          ..style = PaintingStyle.stroke;
        canvas.drawOval(Rect.fromCenter(center: p.screen, width: radius * 2.2, height: radius * 0.65), ringPaint);

        canvas.save();
        canvas.translate(p.screen.dx, p.screen.dy);
        canvas.rotate(pi / 3);
        canvas.drawOval(Rect.fromCenter(center: Offset.zero, width: radius * 2.2, height: radius * 0.65), ringPaint);

        final dotPaint = Paint()..color = Colors.white..style = PaintingStyle.fill;
        double t1 = pulseValue * 2.0 * pi * 1.8;
        canvas.drawCircle(Offset(radius * 1.1 * cos(t1), radius * 0.325 * sin(t1)), 2.0, dotPaint);
        canvas.restore();

        canvas.drawCircle(p.screen, radius * 0.6, Paint()..color = adaptedColor.withValues(alpha: 0.1)..style = PaintingStyle.fill);

        _drawBillboardedIcon(
          canvas,
          p.screen,
          _getBrainIcon(entry.key),
          radius * 0.9 * pulseFactor,
          isMonochrome ? Colors.white : adaptedColor.withValues(alpha: 0.95),
        );
        continue;
      }

      // Handle Tab 8: Cosmic Nebula (Volumetric HSL breathing dust + Core Logo)
      if (activeTab == 7) {
        for (int i = 0; i < 3; i++) {
          double phase = i * 2.0 * pi / 3.0;
          double scaleBreathing = 1.0 + 0.16 * sin(pulseValue * 2.0 * pi * 1.0 + phase);
          Offset offsetBreathing = p.screen + Offset(
            2 * sin(pulseValue * 2.0 * pi * 0.8 + i),
            2 * cos(pulseValue * 2.0 * pi * 0.8 + i),
          );
          final gasPaint = Paint()
            ..shader = RadialGradient(
              colors: [
                adaptedColor.withValues(alpha: 0.25),
                adaptedColor.withValues(alpha: 0.04),
                Colors.transparent,
              ],
            ).createShader(Rect.fromCircle(center: offsetBreathing, radius: radius * 1.7 * scaleBreathing))
            ..style = PaintingStyle.fill;
          canvas.drawCircle(offsetBreathing, radius * 1.7 * scaleBreathing, gasPaint);
        }

        canvas.drawCircle(p.screen, radius * 0.7 * pulseFactor, Paint()..color = adaptedColor.withValues(alpha: 0.18)..maskFilter = const MaskFilter.blur(BlurStyle.normal, 4.0));

        _drawBillboardedIcon(
          canvas,
          p.screen,
          _getBrainIcon(entry.key),
          radius * 0.95 * pulseFactor,
          isMonochrome ? Colors.white : Colors.white.withValues(alpha: 0.95),
        );
        continue;
      }

      // Fallback representing other tabs with Monochrome filter integration
      if (replaceSpheresWithIcons) {
        final category = NeuronVectorLogo.resolveCategory(entry.key);
        final painter = LogoPainter(category: category, overrideColor: adaptedColor);
        canvas.save();
        canvas.translate(p.screen.dx - radius, p.screen.dy - radius);
        painter.paint(canvas, Size(radius * 2, radius * 2));
        canvas.restore();

        if (isSelected) {
          final ringPaint = Paint()
            ..style = PaintingStyle.stroke
            ..strokeWidth = 1.5
            ..color = adaptedColor;
          canvas.drawCircle(p.screen, radius + 4, ringPaint);
        }
      } else {
        canvas.drawCircle(
          p.screen,
          radius + 6,
          Paint()..color = adaptedBase.withValues(alpha: 0.10),
        );

        final bodyRect = Rect.fromCircle(center: p.screen, radius: radius);
        final bodyPaint = Paint()
          ..shader = RadialGradient(
            center: const Alignment(-0.35, -0.45),
            radius: 1.0,
            colors: [
              Color.lerp(adaptedBase, Colors.white, 0.55)!,
              adaptedBase,
              Color.lerp(adaptedBase, Colors.black, 0.35)!,
            ],
            stops: const [0.0, 0.55, 1.0],
          ).createShader(bodyRect);
        canvas.drawCircle(p.screen, radius, bodyPaint);

        canvas.drawCircle(
          p.screen,
          radius,
          Paint()
            ..style = PaintingStyle.stroke
            ..strokeWidth = isSelected ? 1.6 : 1.0
            ..color = adaptedColor.withValues(alpha: isSelected ? 1.0 : 0.85),
        );

        final hi = Offset(
          p.screen.dx - radius * 0.35,
          p.screen.dy - radius * 0.40,
        );
        canvas.drawCircle(
          hi,
          radius * 0.28,
          Paint()..color = Colors.white.withValues(alpha: 0.55),
        );
      }
    }
  }


  void _drawCorrelationRimGlow(
    Canvas canvas,
    Map<String, _Projected> projected,
  ) {
    if (!rimGlowEnabled) return;
    final participating = _participatingNeurons(activeCorrelations, edges);
    final t = (DateTime.now().millisecondsSinceEpoch % 700) / 700.0;
    final pulse = 0.2 + 0.2 * sin(t * 2 * pi).abs();
    final rimPaint = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1.4
      ..color = Colors.white.withValues(alpha: pulse);
    for (final id in participating) {
      final p = projected[id];
      if (p == null) continue;
      canvas.drawCircle(p.screen, 11, rimPaint);
    }
  }

  static Set<String> _participatingNeurons(
    Set<String> active,
    List<GraphEdge> edges,
  ) {
    if (active.isEmpty) return const {};
    final result = <String>{};
    for (final e in edges) {
      if (active.contains(e.correlationId)) {
        result.add(e.fromId);
        result.add(e.toId);
      }
    }
    return result;
  }

  double _depthScale(double depth) {
    final z = _focal + depth;
    return z <= 1 ? 1.0 : _focal / z;
  }

  _Projected _project(vm.Vector3 p, Size size) {
    final v = cam.transform3(p.clone());
    final scale = _depthScale(v.z);
    return _Projected(
      Offset(
        size.width / 2 + v.x * scale * zoom,
        size.height / 2 + v.y * scale * zoom,
      ),
      v.z,
    );
  }

  @override
  bool shouldRepaint(covariant BrainPainter old) => true;

  void _drawLayoutOverlay(Canvas canvas, Size size, Map<String, _Projected> projected) {
    if (activeLayout == 'split_work') {
      final double midX = size.width / 2.0;
      final linePaint = Paint()
        ..color = DigitalBrainColors.teal.withValues(alpha: 0.3)
        ..strokeWidth = 2.0
        ..style = PaintingStyle.stroke;
      const double dashHeight = 10.0;
      const double gapHeight = 10.0;
      double y = 0.0;
      while (y < size.height) {
        canvas.drawLine(Offset(midX, y), Offset(midX, y + dashHeight), linePaint);
        y += dashHeight + gapHeight;
      }

      final textPainter = TextPainter(
        text: TextSpan(
          text: 'SPLIT-WORKSPACE BORDER',
          style: TextStyle(
            color: DigitalBrainColors.teal.withValues(alpha: 0.6),
            fontSize: 10,
            fontWeight: FontWeight.bold,
            letterSpacing: 1.0,
          ),
        ),
        textDirection: TextDirection.ltr,
      )..layout();
      textPainter.paint(canvas, Offset(midX + 12, 24));
    } else if (activeLayout == 'fullscreen') {
      final rect = Offset.zero & size;
      final borderPaint = Paint()
        ..color = DigitalBrainColors.rose.withValues(alpha: 0.4)
        ..strokeWidth = 3.0
        ..style = PaintingStyle.stroke;
      canvas.drawRect(rect.deflate(1.5), borderPaint);

      final glowPaint = Paint()
        ..color = DigitalBrainColors.rose.withValues(alpha: 0.15)
        ..strokeWidth = 8.0
        ..style = PaintingStyle.stroke
        ..maskFilter = const MaskFilter.blur(BlurStyle.normal, 4.0);
      canvas.drawRect(rect.deflate(4.0), glowPaint);

      final textPainter = TextPainter(
        text: TextSpan(
          text: 'FULLSCREEN OVERLAY CONTAINER BOUNDARY',
          style: TextStyle(
            color: DigitalBrainColors.rose.withValues(alpha: 0.7),
            fontSize: 10,
            fontWeight: FontWeight.bold,
            letterSpacing: 1.0,
          ),
        ),
        textDirection: TextDirection.ltr,
      )..layout();
      textPainter.paint(canvas, const Offset(24, 24));
    }

    if (selectedNeuronId == 'DigitalBrain.SDK.Diagram.DiagramExporterNeuron') {
      final nodeProj = projected['DigitalBrain.SDK.Diagram.DiagramExporterNeuron'];
      if (nodeProj != null) {
        final origin = nodeProj.screen + const Offset(40, -40);

        final rect = Rect.fromLTWH(origin.dx, origin.dy, 120, 90);
        final bgPaint = Paint()..color = const Color(0x990A0617);
        final borderPaint = Paint()
          ..style = PaintingStyle.stroke
          ..strokeWidth = 1.0
          ..color = Colors.white.withValues(alpha: 0.15);
        canvas.drawRRect(RRect.fromRectAndRadius(rect, const Radius.circular(8)), bgPaint);
        canvas.drawRRect(RRect.fromRectAndRadius(rect, const Radius.circular(8)), borderPaint);

        final titlePainter = TextPainter(
          text: const TextSpan(
            text: 'LAYOUT MAP',
            style: TextStyle(
              color: Colors.white70,
              fontSize: 8,
              fontWeight: FontWeight.bold,
              letterSpacing: 0.5,
            ),
          ),
          textDirection: TextDirection.ltr,
        )..layout();
        titlePainter.paint(canvas, origin + const Offset(8, 6));

        final double boxWidth = 30.0;
        final double boxHeight = 24.0;
        final double yPos = origin.dy + 24.0;

        final defaultRect = Rect.fromLTWH(origin.dx + 8, yPos, boxWidth, boxHeight);
        final isDefault = activeLayout == 'default';
        canvas.drawRect(defaultRect, Paint()..color = isDefault ? DigitalBrainColors.indigo.withValues(alpha: 0.3) : const Color(0x33FFFFFF));
        canvas.drawRect(defaultRect, Paint()..style = PaintingStyle.stroke..color = isDefault ? DigitalBrainColors.indigo : Colors.white24);

        final splitRect = Rect.fromLTWH(origin.dx + 44, yPos, boxWidth, boxHeight);
        final isSplit = activeLayout == 'split_work';
        canvas.drawRect(splitRect, Paint()..color = isSplit ? DigitalBrainColors.teal.withValues(alpha: 0.3) : const Color(0x33FFFFFF));
        canvas.drawRect(splitRect, Paint()..style = PaintingStyle.stroke..color = isSplit ? DigitalBrainColors.teal : Colors.white24);
        canvas.drawLine(Offset(splitRect.left + 15, splitRect.top), Offset(splitRect.left + 15, splitRect.bottom), Paint()..color = isSplit ? DigitalBrainColors.teal : Colors.white30);

        final fullRect = Rect.fromLTWH(origin.dx + 80, yPos, boxWidth, boxHeight);
        final isFull = activeLayout == 'fullscreen';
        canvas.drawRect(fullRect, Paint()..color = isFull ? DigitalBrainColors.rose.withValues(alpha: 0.3) : const Color(0x33FFFFFF));
        canvas.drawRect(fullRect, Paint()..style = PaintingStyle.stroke..color = isFull ? DigitalBrainColors.rose : Colors.white24);

        final labelPainter = TextPainter(
          text: TextSpan(
            text: 'ACTIVE: ${activeLayout.toUpperCase()}',
            style: TextStyle(
              color: activeLayout == 'fullscreen' ? DigitalBrainColors.rose : (activeLayout == 'split_work' ? DigitalBrainColors.teal : DigitalBrainColors.indigoSoft),
              fontSize: 8,
              fontWeight: FontWeight.bold,
            ),
          ),
          textDirection: TextDirection.ltr,
        )..layout();
        labelPainter.paint(canvas, origin + const Offset(8, 54));

        final filamentPaint = Paint()
          ..color = (activeLayout == 'fullscreen' ? DigitalBrainColors.rose : (activeLayout == 'split_work' ? DigitalBrainColors.teal : DigitalBrainColors.indigoSoft)).withValues(alpha: 0.35)
          ..strokeWidth = 1.0;
        canvas.drawLine(nodeProj.screen, origin + const Offset(0, 45), filamentPaint);
      }
    }
  }

  Color _adaptColor(Color c) {
    if (!isMonochrome) return c;
    final double l = (0.299 * c.red + 0.587 * c.green + 0.114 * c.blue) / 255.0;
    final int gray = (l * 255.0).round();
    return Color.fromARGB(c.alpha, gray, gray, (gray * 1.06).clamp(0, 255).round());
  }

  IconData _getBrainIcon(String id) {
    final lower = id.toLowerCase();
    if (lower.contains('gmail') || lower.contains('mail')) return Icons.mail;
    if (lower.contains('ai') || lower.contains('creator')) return Icons.psychology;
    if (lower.contains('travel')) return Icons.flight;
    if (lower.contains('sqlite') || lower.contains('postgres') || lower.contains('db')) return Icons.dns;
    if (lower.contains('taskmanager')) return Icons.task_alt;
    if (lower.contains('identity') || lower.contains('license')) return Icons.admin_panel_settings;
    if (lower.contains('word')) return Icons.description;
    if (lower.contains('diagram')) return Icons.schema;
    if (lower.contains('onboarding')) return Icons.handshake;
    if (lower.contains('marketplace')) return Icons.storefront;
    if (lower.contains('innolang') || lower.contains('interpreter')) return Icons.translate;
    return Icons.cloud_outlined;
  }

  void _drawBillboardedIcon(Canvas canvas, Offset center2d, IconData iconData, double size, Color color) {
    final iconPainter = TextPainter(
      text: TextSpan(
        text: String.fromCharCode(iconData.codePoint),
        style: TextStyle(
          fontFamily: 'MaterialIcons',
          fontSize: size,
          color: color,
        ),
      ),
      textDirection: TextDirection.ltr,
    )..layout();
    
    iconPainter.paint(
      canvas,
      center2d - Offset(iconPainter.width / 2, iconPainter.height / 2),
    );
  }
}


class _Projected {
  _Projected(this.screen, this.depth);
  final Offset screen;
  final double depth;
}

// Public so other widgets (cluster_label, neuron_label, floating_card) can
// reuse the same projection without duplicating the camera math.
class Projection {
  Projection({required this.cam, required this.zoom, required this.size});
  final vm.Matrix4 cam;
  final double zoom;
  final Size size;
  static const double focal = 700.0;

  ({Offset screen, double depth}) project(vm.Vector3 p) {
    final v = cam.transform3(p.clone());
    final z = focal + v.z;
    final scale = z <= 1 ? 1.0 : focal / z;
    return (
      screen: Offset(
        size.width / 2 + v.x * scale * zoom,
        size.height / 2 + v.y * scale * zoom,
      ),
      depth: v.z,
    );
  }
}
