import 'dart:async';
import 'dart:math' as math;

import 'package:flutter/gestures.dart';
import 'package:flutter/material.dart';
import 'package:flutter/scheduler.dart';

import '../../lumen/neuron_icon.dart';
import '../../theme/ui_theme.dart';
import 'graph_camera.dart';
import 'graph_models.dart';
import 'graph_scene.dart';
import 'spatial_layout.dart';
import 'three_graph_scene.dart';

/// Open-space, GPU-rendered graph. The scene seam allows widget tests to run
/// without a native GL context.
final class SpatialGraph extends StatefulWidget {
  const SpatialGraph({
    super.key,
    required this.nodes,
    required this.edges,
    this.tracePath = const [],
    this.pulses = const [],
    this.active = true,
    this.playing = true,
    this.now,
    this.onNodeTap,
    this.onEdgeTap,
    this.onFallback,
    this.selectedEdgeId,
    this.selectedNodeId,
    this.sceneFactory,
  });

  final List<GraphNode> nodes;
  final List<GraphEdge> edges;
  final List<GraphEdge> tracePath;
  final List<GraphPulse> pulses;
  final bool active;
  final bool playing;
  final DateTime? now;
  final ValueChanged<GraphNode>? onNodeTap;
  final ValueChanged<GraphEdge>? onEdgeTap;
  final VoidCallback? onFallback;
  final String? selectedEdgeId;
  final String? selectedNodeId;
  final GraphSceneFactory? sceneFactory;

  @override
  State<SpatialGraph> createState() => _SpatialGraphState();
}

final class _SpatialGraphState extends State<SpatialGraph>
    with SingleTickerProviderStateMixin {
  late final GraphScene _scene = _createScene();
  final GraphCamera _camera = GraphCamera();
  final ValueNotifier<int> _frames = ValueNotifier(0);
  late final Ticker _ticker;
  Duration _last = Duration.zero;
  Set<String> _knownPulseIds = {};
  bool _reduceMotion = false;
  bool _sceneFailed = false;

  GraphScene _createScene() {
    try {
      return (widget.sceneFactory ?? ThreeGraphScene.new)();
    } catch (_) {
      return _UnavailableGraphScene(widget.onFallback);
    }
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    _reduceMotion = MediaQuery.disableAnimationsOf(context);
  }

  @override
  void initState() {
    super.initState();
    _knownPulseIds = {for (final pulse in widget.pulses) pulse.signature};
    _load(const []);
    if (_scene case final GraphSceneReadiness readiness) {
      readiness.ready.addListener(_onSceneReady);
      readiness.failed.addListener(_onSceneFailure);
    }
    _ticker = createTicker((elapsed) {
      final seconds = (elapsed - _last).inMicroseconds / 1e6;
      _last = elapsed;
      if (seconds <= 0) return;
      final cameraMoving = !_camera.settled;
      if (cameraMoving) {
        _camera.tick(seconds);
        _scene.applyCamera(_camera.current);
        _frames.value++;
      }
      if (widget.playing) {
        if (_scene case final AnimatedGraphScene animated) {
          animated.advance(seconds);
        }
        if (widget.active &&
            widget.pulses.any((pulse) {
              final age = pulse.ageAt(DateTime.now().toUtc());
              return pulse.at != null && age >= 0 && age < 1;
            })) {
          _frames.value++;
        }
      }
    })..start();
    _ticker.muted = !widget.active;
  }

  void _onSceneReady() => _frames.value++;
  void _onSceneFailure() {
    if (mounted) setState(() => _sceneFailed = true);
  }

  void _load(List<GraphPulse> freshPulses) {
    unawaited(
      _scene
          .load(widget.nodes, widget.edges, spatialLayoutGraph(widget.nodes))
          .catchError((Object error, StackTrace stack) {
            if (mounted) setState(() => _sceneFailed = true);
          }),
    );
    if (_scene case final AnimatedGraphScene animated) {
      final now = DateTime.now().toUtc();
      animated.setPulses(
        _reduceMotion || !widget.playing
            ? const []
            : [
                for (final pulse in freshPulses)
                  if (pulse.at != null &&
                      pulse.ageAt(now) >= 0 &&
                      pulse.travelAt(now) < 1)
                    pulse,
              ],
      );
    }
  }

  @override
  void didUpdateWidget(covariant SpatialGraph oldWidget) {
    super.didUpdateWidget(oldWidget);
    _ticker.muted = !widget.active;
    if (oldWidget.tracePath != widget.tracePath) {
      _frames.value++;
    }
    if (oldWidget.nodes != widget.nodes ||
        oldWidget.edges != widget.edges ||
        oldWidget.pulses != widget.pulses) {
      final fresh = [
        for (final pulse in widget.pulses)
          if (!_knownPulseIds.contains(pulse.signature)) pulse,
      ];
      _knownPulseIds = {for (final pulse in widget.pulses) pulse.signature};
      _load(fresh);
    }
  }

  @override
  void dispose() {
    _ticker.dispose();
    if (_scene case final GraphSceneReadiness readiness) {
      readiness.ready.removeListener(_onSceneReady);
      readiness.failed.removeListener(_onSceneFailure);
    }
    _frames.dispose();
    _scene.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => Semantics(
    label: 'Spatial neuron graph. Drag to orbit, scroll to zoom, tap a neuron to inspect it.',
    child: Listener(
      onPointerSignal: (event) {
        if (event is PointerScrollEvent) {
          _camera.zoomBy(event.scrollDelta.dy > 0 ? 0.92 : 1.09);
        }
      },
      child: GestureDetector(
        key: const Key('spatial_graph'),
        behavior: HitTestBehavior.opaque,
        onPanUpdate: (details) =>
            _camera.orbitBy(details.delta.dx, details.delta.dy),
        onTapUp: (details) {
          String? id = _scene.pick(details.localPosition);
          if (id == null) {
            for (final node in widget.nodes) {
              final point = _scene.project(node.id);
              if (point != null &&
                  (point - details.localPosition).distance <= 26) {
                id = node.id;
                break;
              }
            }
          }
          if (id == null) {
            for (final edge in widget.edges) {
              final from = _scene.project(edge.sourceId);
              final to = _scene.project(edge.targetId);
              if (from == null || to == null) continue;
              final delta = to - from;
              if (delta.distanceSquared < 1) continue;
              final tap = details.localPosition - from;
              final fraction =
                  ((tap.dx * delta.dx + tap.dy * delta.dy) /
                          delta.distanceSquared)
                      .clamp(0.0, 1.0);
              if ((from + delta * fraction - details.localPosition).distance <=
                  10) {
                widget.onEdgeTap?.call(edge);
                break;
              }
            }
            return;
          }
          for (final node in widget.nodes) {
            if (node.id == id) {
              widget.onNodeTap?.call(node);
              _camera.focusOn(spatialLayoutGraph(widget.nodes)[id]!);
              break;
            }
          }
        },
        child: Stack(
          fit: StackFit.expand,
          children: [
            _sceneFailed || _scene is _UnavailableGraphScene
                ? _UnavailableGraphScene(widget.onFallback).build(context)
                : IgnorePointer(child: _scene.build(context)),
            ValueListenableBuilder<int>(
              valueListenable: _frames,
              builder: (context, _, _) => IgnorePointer(
                child: Stack(
                  children: [
                    Positioned.fill(
                      child: CustomPaint(
                        key: const Key('spatial_connections'),
                        painter: _SpatialConnectionPainter(
                          widget.edges,
                          widget.tracePath,
                          {
                            for (final node in widget.nodes)
                              node.id: ?_scene.project(node.id),
                          },
                        ),
                      ),
                    ),
                    for (final edge in widget.edges)
                      if (edge.id == widget.selectedEdgeId &&
                          _scene.project(edge.sourceId) != null &&
                          _scene.project(edge.targetId) != null)
                        Positioned.fill(
                          child: CustomPaint(
                            key: Key('spatial_selected_route_${edge.id}'),
                            painter: _SelectedRoutePainter(
                              _scene.project(edge.sourceId)!,
                              _scene.project(edge.targetId)!,
                            ),
                          ),
                        ),
                    Positioned.fill(
                      child: CustomPaint(
                        painter: _SpatialTrailPainter(
                          widget.pulses,
                          {
                            for (final node in widget.nodes)
                              node.id: ?_scene.project(node.id),
                          },
                          widget.now ?? DateTime.now().toUtc(),
                          _reduceMotion,
                        ),
                      ),
                    ),
                    for (final node in widget.nodes)
                      if (_scene.project(node.id) case final point?)
                        Positioned(
                          left: point.dx - 14,
                          top: point.dy - 35,
                          child: Column(
                            mainAxisSize: MainAxisSize.min,
                            children: [
                              DecoratedBox(
                                decoration: BoxDecoration(
                                  shape: BoxShape.circle,
                                  border: node.id == widget.selectedNodeId
                                      ? Border.all(
                                          color: Colors.amberAccent,
                                          width: 2,
                                        )
                                      : null,
                                ),
                                child: NeuronIcon(
                                  kind: NeuronIconKind.fromKey(node.iconKey),
                                  size: 22,
                                ),
                              ),
                              Text(
                                node.label,
                                style: const TextStyle(fontSize: 11),
                              ),
                            ],
                          ),
                        ),
                  ],
                ),
              ),
            ),
            Positioned(
              right: 8,
              top: 8,
              child: IconButton(
                tooltip: 'Fit 3D graph',
                icon: const Icon(Icons.center_focus_strong_outlined),
                onPressed: _camera.reset,
              ),
            ),
          ],
        ),
      ),
    ),
  );
}

final class _SelectedRoutePainter extends CustomPainter {
  const _SelectedRoutePainter(this.from, this.to);
  final Offset from;
  final Offset to;

  @override
  void paint(Canvas canvas, Size size) {
    final paint = Paint()
      ..color = Colors.amberAccent
      ..strokeWidth = 3
      ..style = PaintingStyle.stroke;
    canvas.drawLine(from, to, paint);
    canvas.drawCircle(from, 15, paint);
    canvas.drawCircle(to, 15, paint);
  }

  @override
  bool shouldRepaint(covariant _SelectedRoutePainter oldDelegate) =>
      oldDelegate.from != from || oldDelegate.to != to;
}

/// Projected base routes stay legible when there is no recent activity.
final class _SpatialConnectionPainter extends CustomPainter {
  const _SpatialConnectionPainter(this.edges, this.tracePath, this.positions);
  final List<GraphEdge> edges;
  final List<GraphEdge> tracePath;
  final Map<String, Offset> positions;

  @override
  void paint(Canvas canvas, Size size) {
    final line = Paint()
      ..color = const Color(0xff8ea8d8).withValues(alpha: .72)
      ..strokeWidth = 2.2
      ..style = PaintingStyle.stroke;
    for (final edge in edges) {
      final from = positions[edge.sourceId];
      final to = positions[edge.targetId];
      if (from == null || to == null) continue;
      canvas.drawLine(from, to, line);
      final direction = (to - from).direction;
      final tip = from + (to - from) * .82;
      final arrow = Path()
        ..moveTo(tip.dx, tip.dy)
        ..lineTo(
          tip.dx - 9 * math.cos(direction - .5),
          tip.dy - 9 * math.sin(direction - .5),
        )
        ..moveTo(tip.dx, tip.dy)
        ..lineTo(
          tip.dx - 9 * math.cos(direction + .5),
          tip.dy - 9 * math.sin(direction + .5),
        );
      canvas.drawPath(arrow, line);
    }
    _paintTrace(canvas);
  }

  void _paintTrace(Canvas canvas) {
    final totals = <String, int>{};
    for (final edge in tracePath) {
      totals[_pair(edge)] = (totals[_pair(edge)] ?? 0) + 1;
    }
    final seen = <String, int>{};
    for (final edge in tracePath) {
      final label = edge.label;
      if (label == null) continue;
      final from = positions[edge.sourceId];
      final to = positions[edge.targetId];
      if (from == null || to == null) continue;
      final key = _pair(edge);
      final total = totals[key]!;
      final index = seen[key] = (seen[key] ?? 0) + 1;
      final fraction = total > 1 ? index / (total + 1) : 0.5;
      canvas.drawLine(
        from,
        to,
        Paint()
          ..color = UiPalette.signal.withValues(alpha: .95)
          ..strokeWidth = 3.5
          ..style = PaintingStyle.stroke,
      );
      final mid = Offset.lerp(from, to, fraction)!;
      final text = TextPainter(
        text: TextSpan(
          text: label,
          style: UiType.metaStrong.copyWith(color: UiPalette.surface),
        ),
        textDirection: TextDirection.ltr,
      )..layout();
      canvas.drawCircle(
        mid,
        math.max(text.width, text.height) / 2 + 5,
        Paint()..color = UiPalette.signal,
      );
      text.paint(canvas, mid - Offset(text.width / 2, text.height / 2));
    }
  }

  static String _pair(GraphEdge edge) => '${edge.sourceId}|${edge.targetId}';

  @override
  bool shouldRepaint(covariant _SpatialConnectionPainter oldDelegate) =>
      oldDelegate.edges != edges ||
      oldDelegate.tracePath != tracePath ||
      oldDelegate.positions != positions;
}

/// Bright, bounded activity remains readable even after the GL particle ends.
final class _SpatialTrailPainter extends CustomPainter {
  const _SpatialTrailPainter(
    this.pulses,
    this.positions,
    this.now,
    this.reducedMotion,
  );
  final List<GraphPulse> pulses;
  final Map<String, Offset> positions;
  final DateTime now;
  final bool reducedMotion;

  @override
  void paint(Canvas canvas, Size size) {
    for (final pulse in pulses) {
      final age = pulse.ageAt(now);
      if (age < 0 || age >= 1) continue;
      final from = positions[pulse.fromId];
      final to = positions[pulse.toId];
      if (from == null || to == null) continue;
      final alpha = pulse.opacityAt(now);
      final color = switch (pulse.outcome) {
        GraphPulseOutcome.signal => const Color(0xff43e4a1),
        GraphPulseOutcome.failed => const Color(0xfff06978),
        GraphPulseOutcome.completed => const Color(0xff7ee6ca),
        _ => const Color(0xfffff0bd),
      };
      if (!pulse.local) {
        final control = Offset(
          (from.dx + to.dx) / 2,
          (from.dy + to.dy) / 2 - math.min(35, (to - from).distance * .18),
        );
        final path = Path()
          ..moveTo(from.dx, from.dy)
          ..quadraticBezierTo(control.dx, control.dy, to.dx, to.dy);
        canvas.drawPath(
          path,
          Paint()
            ..style = PaintingStyle.stroke
            ..strokeWidth = 2 + 4 * alpha
            ..color = color.withValues(alpha: .12 + alpha * .85),
        );
        final travel = pulse.travelAt(now);
        if (travel < 1 && !reducedMotion) {
          final metric = path.computeMetrics().first;
          final head = metric.getTangentForOffset(metric.length * travel);
          final tail = metric.getTangentForOffset(
            (metric.length * travel - 25).clamp(0.0, metric.length),
          );
          if (head != null && tail != null) {
            canvas.drawLine(
              tail.position,
              head.position,
              Paint()
                ..strokeWidth = 7
                ..strokeCap = StrokeCap.round
                ..color = color.withValues(alpha: alpha),
            );
          }
        }
      }
      final center = pulse.local || reducedMotion || pulse.travelAt(now) >= .85
          ? to
          : from;
      canvas.drawCircle(
        center,
        19 + (1 - alpha) * 12,
        Paint()
          ..style = PaintingStyle.stroke
          ..strokeWidth = 2.5
          ..color = color.withValues(alpha: alpha * .8),
      );
    }
  }

  @override
  bool shouldRepaint(covariant _SpatialTrailPainter oldDelegate) =>
      oldDelegate.now != now ||
      oldDelegate.reducedMotion != reducedMotion ||
      oldDelegate.pulses != pulses ||
      oldDelegate.positions != positions;
}

final class _UnavailableGraphScene implements GraphScene {
  const _UnavailableGraphScene(this.onFallback);
  final VoidCallback? onFallback;

  @override
  Future<void> load(
    List<GraphNode> nodes,
    List<GraphEdge> edges,
    Map<String, GraphPoint> layout,
  ) async {}
  @override
  void applyCamera(GraphCameraState state) {}
  @override
  String? pick(Offset local) => null;
  @override
  Offset? project(String nodeId) => null;
  @override
  Widget build(BuildContext context) => Center(
    child: Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        const Text('3D renderer unavailable'),
        TextButton(onPressed: onFallback, child: const Text('Use Graph view')),
      ],
    ),
  );
  @override
  void dispose() {}
}
