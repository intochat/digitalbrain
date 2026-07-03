import 'dart:async';
import 'dart:convert';
import 'dart:math';
import 'dart:ui';
import 'package:flutter/material.dart';
import 'package:google_fonts/google_fonts.dart';
import 'package:digitalbrain_flutter/grpc/brainwatch.pbgrpc.dart';
import 'package:digitalbrain_flutter/grpc/brainwatch.pb.dart' as bw;
import 'package:digitalbrain_flutter/grpc/digitalbrain.pbgrpc.dart' as gw;
import 'visual_constructor_models.dart';
import 'visual_constructor_state.dart';

class CanvasParticle {
  final Offset start;
  final Offset end;
  final String typeName;
  double progress = 0.0;
  final double speed = 1.0; // speed units per second

  CanvasParticle({
    required this.start,
    required this.end,
    required this.typeName,
  });

  void update(double dt) {
    progress += speed * dt;
  }

  bool get isExpired => progress >= 1.0;
}

class CanvasRipple {
  final Offset center;
  double radius = 0.0;
  final double maxRadius = 380.0;
  final double speed = 240.0; // logical pixels per second
  double opacity = 1.0;

  CanvasRipple({required this.center});

  void update(double dt) {
    radius += speed * dt;
    opacity = (1.0 - (radius / maxRadius)).clamp(0.0, 1.0);
  }

  bool get isExpired => radius >= maxRadius;
}

class VisualConstructorCanvas extends StatefulWidget {
  final VisualConstructorState state;
  final BrainWatchClient? brainWatchClient;
  final gw.DigitalBrainGatewayClient? gatewayClient;

  const VisualConstructorCanvas({
    super.key,
    required this.state,
    this.brainWatchClient,
    this.gatewayClient,
  });

  @override
  State<VisualConstructorCanvas> createState() => _VisualConstructorCanvasState();
}

class _VisualConstructorCanvasState extends State<VisualConstructorCanvas>
    with SingleTickerProviderStateMixin {
  final GlobalKey _canvasKey = GlobalKey();
  late AnimationController _animController;
  DateTime _lastTick = DateTime.now();

  final List<CanvasParticle> _particles = [];
  final List<CanvasRipple> _ripples = [];

  StreamSubscription? _synapseSubscription;

  @override
  void initState() {
    super.initState();
    widget.state.addListener(_onStateChanged);
    widget.state.onSynapseFiredLocally = _triggerVisualPulse;
    _animController = AnimationController(
      vsync: this,
      duration: const Duration(seconds: 10),
    )..repeat();
    _animController.addListener(_onAnimTick);
    _startSynapseListening();
  }

  @override
  void didUpdateWidget(covariant VisualConstructorCanvas oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (widget.brainWatchClient != oldWidget.brainWatchClient) {
      _startSynapseListening();
    }
  }

  @override
  void dispose() {
    widget.state.removeListener(_onStateChanged);
    _synapseSubscription?.cancel();
    _animController.dispose();
    super.dispose();
  }

  void _onStateChanged() {
    if (mounted) setState(() {});
  }

  void _onAnimTick() {
    if (!mounted) return;
    final now = DateTime.now();
    final dt = now.difference(_lastTick).inMilliseconds / 1000.0;
    _lastTick = now;

    // We throb and animate even when lists are empty, so always trigger rebuild
    setState(() {
      for (var i = _particles.length - 1; i >= 0; i--) {
        _particles[i].update(dt);
        if (_particles[i].isExpired) {
          _particles.removeAt(i);
        }
      }

      for (var i = _ripples.length - 1; i >= 0; i--) {
        _ripples[i].update(dt);
        if (_ripples[i].isExpired) {
          _ripples.removeAt(i);
        }
      }
    });
  }

  void _startSynapseListening() {
    final client = widget.brainWatchClient;
    if (client == null) {
      debugPrint("VisualConstructorCanvas: No BrainWatchClient provided, offline/mock mode enabled.");
      return;
    }

    _synapseSubscription?.cancel();
    _synapseSubscription = client
        .watchSynapses(bw.WatchSynapsesRequest(brainId: 'default'))
        .listen(
      (edge) {
        debugPrint("VisualConstructorCanvas received live synapse: ${edge.typeName}");
        _triggerVisualPulse(edge.typeName);

        if (edge.typeName.contains("CanvasRenderEvent")) {
          try {
            final payloadStr = utf8.decode(edge.payload);
            final data = jsonDecode(payloadStr);
            final nodeId = data['NodeId'] ?? data['nodeId'];
            final label = data['Label'] ?? data['label'];
            final x = (data['X'] ?? data['x'] ?? 100.0).toDouble();
            final y = (data['Y'] ?? data['y'] ?? 100.0).toDouble();
            final tone = data['Tone'] ?? data['tone'] ?? 'cyan';
            
            if (nodeId != null && label != null) {
              widget.state.addConceptNode(nodeId, label, Offset(x, y), tone);
              
              // Visual effect: fire a pulse particle to the newly generated concept node!
              Future.delayed(const Duration(milliseconds: 100), () {
                if (mounted) {
                  _triggerVisualPulse(edge.typeName);
                }
              });
            }
          } catch (e) {
            debugPrint("Error parsing CanvasRenderEvent payload: $e");
          }
        }
      },
      onError: (err) {
        debugPrint("Error in VisualConstructorCanvas watchSynapses: $err");
      },
    );
  }

  void _triggerVisualPulse(String typeName) {
    if (!mounted) return;

    if (typeName.contains("TranslateTextRequest") ||
        typeName.contains("TextTranslatedEvent")) {
      final fromNode = widget.state.nodes['llm_translation_neuron'];
      final toNode = widget.state.nodes['llm_alerting_neuron'];
      if (fromNode != null && toNode != null) {
        final fromPort = fromNode.ports.firstWhere((p) => p.id == 'translation_out');
        final toPort = toNode.ports.firstWhere((p) => p.id == 'alerting_in');
        final start = _getGlobalPortOffset(fromNode, fromPort);
        final end = _getGlobalPortOffset(toNode, toPort);

        setState(() {
          _particles.add(CanvasParticle(
            start: start,
            end: end,
            typeName: typeName,
          ));
        });
      }
    } else if (typeName.contains("SystemAlertFiredEvent")) {
      final node = widget.state.nodes['llm_alerting_neuron'];
      if (node != null) {
        final center = node.position + const Offset(90, 50);
        setState(() {
          _ripples.add(CanvasRipple(center: center));
        });
      }
    }
  }

  Offset _getGlobalPortOffset(VisualNode node, NodePort port) {
    return node.position + port.relativeOffset;
  }

  @override
  Widget build(BuildContext context) {
    return GestureDetector(
      key: _canvasKey,
      onTap: () {
        widget.state.selectNode(null);
      },
      child: Container(
        color: Colors.transparent, // Intercept taps on empty space
        child: Stack(
          clipBehavior: Clip.none,
          children: [
            // 1. Grid Background
            Positioned.fill(
              child: CustomPaint(
                painter: _CanvasGridPainter(
                  panOffset: widget.state.panOffset,
                  zoomScale: widget.state.zoomScale,
                ),
              ),
            ),

            // 2. Connection Lines (Bezier Wires)
            ...widget.state.connections.map((conn) {
              final fromNode = widget.state.nodes[conn.fromNodeId];
              final toNode = widget.state.nodes[conn.toNodeId];
              if (fromNode == null || toNode == null) return const SizedBox.shrink();

              final fromPort = fromNode.ports.firstWhere((p) => p.id == conn.fromPortId);
              final toPort = toNode.ports.firstWhere((p) => p.id == conn.toPortId);

              final start = _getGlobalPortOffset(fromNode, fromPort);
              final end = _getGlobalPortOffset(toNode, toPort);

              return CustomPaint(
                painter: _ConnectionPainter(
                  start: start,
                  end: end,
                  color: const Color(0xFFFF9F1C).withValues(alpha: 0.6), // Frosted amber line
                ),
              );
            }),

            // 3. Active Drag Cable
            if (widget.state.dragStartPos != null && widget.state.dragCurrentPos != null)
              CustomPaint(
                painter: _ConnectionPainter(
                  start: widget.state.dragStartPos!,
                  end: widget.state.dragCurrentPos!,
                  color: const Color(0xFFFF9F1C).withValues(alpha: 0.8),
                  isDashed: true,
                ),
              ),

            // 4. Custom Paint Synapse Animations (Particles & Shockwaves)
            Positioned.fill(
              child: CustomPaint(
                painter: _SynapseAnimationPainter(
                  particles: _particles,
                  ripples: _ripples,
                ),
              ),
            ),

            // 5. Interactive Nodes (Floating 3D Orbs)
            ...widget.state.nodes.values.map((node) {
              final isSelected = widget.state.selectedNodeId == node.id;
              final isTranslation = node.id == 'llm_translation_neuron';
              final isAlerting = node.id == 'llm_alerting_neuron';

              return Positioned(
                left: node.position.dx,
                top: node.position.dy,
                child: GestureDetector(
                  onTap: () {
                    widget.state.selectNode(node.id);
                  },
                  onPanUpdate: (details) {
                    widget.state.updateNodePosition(node.id, details.delta);
                  },
                  child: Stack(
                    clipBehavior: Clip.none,
                    children: [
                      // Floating 3D Orb Graphic
                      CustomPaint(
                        size: node.size,
                        painter: _Neuron3DOrbPainter(
                          animationValue: _animController.value,
                          themeColor: isTranslation
                              ? const Color(0xFF00FFD2) // Neon Cyan
                              : (isAlerting
                                  ? const Color(0xFFFF2D55) // Neon Red/Pink
                                  : const Color(0xFF5856D6)), // Purple
                          isSelected: isSelected,
                        ),
                      ),

                      // Ingress/Egress Ports (Interactive)
                      ...node.ports.where((p) => p.isInput).map((port) {
                        return Positioned(
                          left: port.relativeOffset.dx - 6,
                          top: port.relativeOffset.dy - 6,
                          child: Container(
                            width: 12,
                            height: 12,
                            decoration: BoxDecoration(
                              color: const Color(0xFF27272A),
                              shape: BoxShape.circle,
                              border: Border.all(color: Colors.white70, width: 2),
                            ),
                          ),
                        );
                      }),

                      ...node.ports.where((p) => !p.isInput).map((port) {
                        return Positioned(
                          left: port.relativeOffset.dx - 6,
                          top: port.relativeOffset.dy - 6,
                          child: Container(
                            width: 12,
                            height: 12,
                            decoration: BoxDecoration(
                              color: const Color(0xFFFF9F1C),
                              shape: BoxShape.circle,
                              border: Border.all(color: Colors.white, width: 2),
                              boxShadow: [
                                BoxShadow(
                                  color: const Color(0xFFFF9F1C).withValues(alpha: 0.5),
                                  blurRadius: 8,
                                ),
                              ],
                            ),
                          ),
                        );
                      }),

                      // Foreground text label overlays
                      Positioned(
                        top: -24,
                        left: 0,
                        right: 0,
                        child: Center(
                          child: ClipRRect(
                            borderRadius: BorderRadius.circular(8),
                            child: BackdropFilter(
                              filter: ImageFilter.blur(sigmaX: 8, sigmaY: 8),
                              child: Container(
                                padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
                                decoration: BoxDecoration(
                                  color: Colors.black45,
                                  borderRadius: BorderRadius.circular(8),
                                  border: Border.all(
                                    color: Colors.white12,
                                    width: 0.5,
                                  ),
                                ),
                                child: Text(
                                  node.label,
                                  style: GoogleFonts.jetBrainsMono(
                                    color: Colors.white,
                                    fontSize: 10,
                                    fontWeight: FontWeight.bold,
                                  ),
                                ),
                              ),
                            ),
                          ),
                        ),
                      ),
                    ],
                  ),
                ),
              );
            }),
          ],
        ),
      ),
    );
  }
}

class _CanvasGridPainter extends CustomPainter {
  final Offset panOffset;
  final double zoomScale;

  _CanvasGridPainter({required this.panOffset, required this.zoomScale});

  @override
  void paint(Canvas canvas, Size size) {
    final paint = Paint()
      ..color = Colors.white.withValues(alpha: 0.015)
      ..strokeWidth = 1.0;

    const double gridSize = 30.0;

    for (double x = 0; x < size.width; x += gridSize) {
      canvas.drawLine(Offset(x, 0), Offset(x, size.height), paint);
    }
    for (double y = 0; y < size.height; y += gridSize) {
      canvas.drawLine(Offset(0, y), Offset(size.width, y), paint);
    }
  }

  @override
  bool shouldRepaint(covariant _CanvasGridPainter oldDelegate) {
    return panOffset != oldDelegate.panOffset || zoomScale != oldDelegate.zoomScale;
  }
}

class _ConnectionPainter extends CustomPainter {
  final Offset start;
  final Offset end;
  final Color color;
  final bool isDashed;

  _ConnectionPainter({
    required this.start,
    required this.end,
    required this.color,
    this.isDashed = false,
  });

  @override
  void paint(Canvas canvas, Size size) {
    final paint = Paint()
      ..color = color
      ..strokeWidth = 2.0
      ..style = PaintingStyle.stroke;

    final path = Path();
    path.moveTo(start.dx, start.dy);

    final controlOffset = max((end.dx - start.dx).abs() * 0.5, 30.0);
    final cp1 = Offset(start.dx + controlOffset, start.dy);
    final cp2 = Offset(end.dx - controlOffset, end.dy);

    path.cubicTo(cp1.dx, cp1.dy, cp2.dx, cp2.dy, end.dx, end.dy);

    if (isDashed) {
      final dashedPath = Path();
      const double dashWidth = 8.0;
      const double dashSpace = 4.0;
      double distance = 0.0;

      for (final metric in path.computeMetrics()) {
        while (distance < metric.length) {
          dashedPath.addPath(
            metric.extractPath(distance, distance + dashWidth),
            Offset.zero,
          );
          distance += dashWidth + dashSpace;
        }
      }
      canvas.drawPath(dashedPath, paint);
    } else {
      canvas.drawPath(path, paint);
    }
  }

  @override
  bool shouldRepaint(covariant _ConnectionPainter oldDelegate) {
    return start != oldDelegate.start || end != oldDelegate.end || color != oldDelegate.color;
  }
}

class _Neuron3DOrbPainter extends CustomPainter {
  final double animationValue;
  final Color themeColor;
  final bool isSelected;

  _Neuron3DOrbPainter({
    required this.animationValue,
    required this.themeColor,
    required this.isSelected,
  });

  @override
  void paint(Canvas canvas, Size size) {
    final center = Offset(size.width / 2, size.height / 2);
    final baseRadius = min(size.width, size.height) / 2 - 8;

    // Oscillating Z-depth factor for breath throbbing
    final time = animationValue * 2 * pi;
    final zOffset = sin(time) * 0.08;
    final scale = 1.0 + zOffset;
    final radius = baseRadius * scale;

    // Glowing shadow
    final glowPaint = Paint()
      ..color = themeColor.withValues(alpha: isSelected ? 0.35 : 0.15)
      ..maskFilter = MaskFilter.blur(BlurStyle.normal, isSelected ? 24.0 : 12.0);
    canvas.drawCircle(center, radius + 4, glowPaint);

    // 3D diffuse & specular radial shader
    final spherePaint = Paint()
      ..shader = RadialGradient(
        center: const Alignment(-0.3, -0.3), // offset light source
        radius: 0.95,
        colors: [
          Colors.white.withValues(alpha: 0.95), // hot reflection spot
          themeColor.withValues(alpha: 0.8),
          themeColor.darken(0.3).withValues(alpha: 0.9), // dark border/shade
          Colors.black87,
        ],
        stops: const [0.0, 0.25, 0.75, 1.0],
      ).createShader(Rect.fromCircle(center: center, radius: radius));

    canvas.drawCircle(center, radius, spherePaint);

    // Subtle floating planetary/orbital ring around the orb
    final ringPaint = Paint()
      ..color = themeColor.withValues(alpha: isSelected ? 0.75 : 0.35)
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1.5;

    canvas.save();
    canvas.translate(center.dx, center.dy);
    canvas.rotate(0.3); // tilt ring
    canvas.drawOval(
      Rect.fromCenter(center: Offset.zero, width: radius * 2.3, height: radius * 0.45),
      ringPaint,
    );
    canvas.restore();
  }

  @override
  bool shouldRepaint(covariant _Neuron3DOrbPainter oldDelegate) => true;
}

class _SynapseAnimationPainter extends CustomPainter {
  final List<CanvasParticle> particles;
  final List<CanvasRipple> ripples;

  _SynapseAnimationPainter({
    required this.particles,
    required this.ripples,
  });

  Offset getBezierPoint(Offset start, Offset end, double t) {
    final controlOffset = max((end.dx - start.dx).abs() * 0.5, 30.0);
    final cp1 = Offset(start.dx + controlOffset, start.dy);
    final cp2 = Offset(end.dx - controlOffset, end.dy);

    final double u = 1.0 - t;
    final double tt = t * t;
    final double uu = u * u;
    final double uuu = uu * u;
    final double ttt = tt * t;

    final p = (start * uuu) +
        (cp1 * 3 * uu * t) +
        (cp2 * 3 * u * tt) +
        (end * ttt);
    return p;
  }

  @override
  void paint(Canvas canvas, Size size) {
    // 1. Paint point-to-point sliding neon particles
    for (final p in particles) {
      final pos = getBezierPoint(p.start, p.end, p.progress);

      final outerGlow = Paint()
        ..color = const Color(0xFF00FFD2).withOpacity(0.6)
        ..maskFilter = const MaskFilter.blur(BlurStyle.normal, 12.0);
      canvas.drawCircle(pos, 12.0, outerGlow);

      final midPulse = Paint()
        ..color = const Color(0xFF00FFD2)
        ..style = PaintingStyle.fill;
      canvas.drawCircle(pos, 6.0, midPulse);

      final core = Paint()
        ..color = Colors.white
        ..style = PaintingStyle.fill;
      canvas.drawCircle(pos, 3.0, core);
    }

    // 2. Paint expanding 3D broadcast shockwave ripples
    for (final r in ripples) {
      canvas.save();
      // Tilt tilted back in Z-axis space for 3D depth perspective!
      final matrix = Matrix4.identity()
        ..translate(r.center.dx, r.center.dy)
        ..rotateX(0.9)
        ..translate(-r.center.dx, -r.center.dy);
      canvas.transform(matrix.storage);

      final ripplePaint = Paint()
        ..color = const Color(0xFFFF2D55).withOpacity(r.opacity * 0.7)
        ..style = PaintingStyle.stroke
        ..strokeWidth = 3.0
        ..maskFilter = const MaskFilter.blur(BlurStyle.normal, 3.0);

      canvas.drawCircle(r.center, r.radius, ripplePaint);

      // Inner faint atmospheric expand ring
      final innerAtm = Paint()
        ..color = const Color(0xFFFF2D55).withOpacity(r.opacity * 0.15)
        ..style = PaintingStyle.fill;
      canvas.drawCircle(r.center, r.radius, innerAtm);

      canvas.restore();
    }
  }

  @override
  bool shouldRepaint(covariant _SynapseAnimationPainter oldDelegate) => true;
}

extension ColorDarken on Color {
  Color darken([double amount = .1]) {
    assert(amount >= 0 && amount <= 1);
    final hsl = HSLColor.fromColor(this);
    final hslDark = hsl.withLightness((hsl.lightness - amount).clamp(0.0, 1.0));
    return hslDark.toColor();
  }
}
