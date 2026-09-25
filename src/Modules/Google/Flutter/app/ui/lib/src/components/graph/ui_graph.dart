import 'dart:async';

import 'package:flutter/material.dart';

import 'graph_geometry.dart';
import 'graph_models.dart';
import 'graph_painter.dart';
import '../../lumen/neuron_icon.dart';

/// Product graph control: depth-projected nodes with directed edges.
/// Same widget for surface galleries, the Brain view, and future diagrams.
final class UiGraph extends StatefulWidget {
  const UiGraph({
    super.key,
    required this.nodes,
    required this.edges,
    this.pulse,
    this.pulses = const [],
    this.highlightEdgeId,
    this.now,
    this.playing = true,
    this.onNodeTap,
    this.onEdgeTap,
    this.semanticsLabel = 'Interactive three-dimensional graph. Drag to rotate; tap a node or edge to inspect it.',
  });

  final List<GraphNode> nodes;
  final List<GraphEdge> edges;
  final GraphPulse? pulse;
  final List<GraphPulse> pulses;
  final String? highlightEdgeId;
  final DateTime? now;
  final bool playing;
  final ValueChanged<GraphNode>? onNodeTap;
  final ValueChanged<GraphEdge>? onEdgeTap;
  final String semanticsLabel;

  @override
  State<UiGraph> createState() => _UiGraphState();
}

final class _UiGraphState extends State<UiGraph>
    with SingleTickerProviderStateMixin {
  late final AnimationController _pulse;
  final TransformationController _viewport = TransformationController();
  double _rotationX = -0.18;
  double _rotationY = 0.42;
  Timer? _pulseExpiry;
  Timer? _trailTicker;

  @override
  void initState() {
    super.initState();
    _pulse = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 1400),
    );
    if (widget.pulse != null || widget.pulses.isNotEmpty) {
      _pulse.forward();
    }
    _schedulePulseExpiry();
    _syncTrailTicker();
  }

  @override
  void didUpdateWidget(covariant UiGraph oldWidget) {
    super.didUpdateWidget(oldWidget);
    final pulseChanged =
        widget.pulse?.signature != oldWidget.pulse?.signature ||
        widget.pulses.lastOrNull?.signature !=
            oldWidget.pulses.lastOrNull?.signature;
    if (widget.pulse == null && widget.pulses.isEmpty) {
      if (pulseChanged) {
        _pulse.reset();
      }
    } else if (pulseChanged) {
      _pulse.forward(from: 0);
    }
    if (pulseChanged) _schedulePulseExpiry();
    if (pulseChanged || oldWidget.playing != widget.playing) {
      _syncTrailTicker();
    }
  }

  void _syncTrailTicker() {
    _trailTicker?.cancel();
    if (!widget.playing) return;
    _trailTicker = Timer.periodic(const Duration(milliseconds: 60), (timer) {
      final now = DateTime.now().toUtc();
      final visible = [widget.pulse, ...widget.pulses]
          .whereType<GraphPulse>()
          .any(
            (pulse) =>
                pulse.at != null &&
                pulse.ageAt(now) >= 0 &&
                pulse.ageAt(now) < 1,
          );
      if (!visible) {
        timer.cancel();
        return;
      }
      if (mounted) setState(() {});
    });
  }

  void _schedulePulseExpiry() {
    _pulseExpiry?.cancel();
    final now = DateTime.now().toUtc();
    final expiries =
        [widget.pulse, ...widget.pulses]
            .whereType<GraphPulse>()
            .where((pulse) => pulse.at != null)
            .map(
              (pulse) =>
                  (pulse.updatedAt ?? pulse.at!).add(GraphPulse.trailLifetime),
            )
            .where((expiry) => expiry.isAfter(now))
            .toList()
          ..sort();
    if (expiries.isNotEmpty) {
      _pulseExpiry = Timer(expiries.last.difference(now), () {
        if (mounted) setState(() {});
      });
    }
  }

  @override
  void dispose() {
    _pulse.dispose();
    _pulseExpiry?.cancel();
    _trailTicker?.cancel();
    _viewport.dispose();
    super.dispose();
  }

  void _rotate(DragUpdateDetails details) {
    setState(() {
      _rotationY += details.delta.dx * 0.008;
      _rotationX = (_rotationX + details.delta.dy * 0.008).clamp(-1.0, 1.0);
    });
  }

  @override
  Widget build(BuildContext context) {
    final disableAnimations = MediaQuery.disableAnimationsOf(context);

    return Semantics(
      key: const Key('ui_graph'),
      label: widget.semanticsLabel,
      image: true,
      child: LayoutBuilder(
        builder: (context, constraints) {
          final size = Size(constraints.maxWidth, constraints.maxHeight);
          return AnimatedBuilder(
            animation: _pulse,
            builder: (context, _) {
              final projected = projectGraphNodes(
                widget.nodes,
                size,
                _rotationX,
                _rotationY,
              );
              final projectedEdges = projectGraphEdges(widget.edges, projected);
              final canvasCenter = Offset(size.width * 0.5, size.height * 0.51);
              final pulseValue = disableAnimations ? 1.0 : _pulse.value;

              return Stack(
                children: [
                  InteractiveViewer(
                    transformationController: _viewport,
                    minScale: 0.2,
                    maxScale: 4,
                    boundaryMargin: const EdgeInsets.all(1000),
                    child: SizedBox(
                      width: size.width,
                      height: size.height,
                      child: GestureDetector(
                        behavior: HitTestBehavior.opaque,
                        onPanUpdate: _rotate,
                        onTapUp: (details) {
                          final node = hitTestGraphNodes(
                            projected,
                            details.localPosition,
                          );
                          if (node != null) {
                            widget.onNodeTap?.call(node.node);
                            return;
                          }
                          final edge = hitTestGraphEdges(
                            projectedEdges,
                            details.localPosition,
                            canvasCenter,
                          );
                          if (edge != null) {
                            widget.onEdgeTap?.call(edge.edge);
                          }
                        },
                        child: Stack(
                          fit: StackFit.expand,
                          children: [
                            CustomPaint(
                              painter: GraphPainter(
                                nodes: projected,
                                edges: projectedEdges,
                                pulse: widget.pulse,
                                pulses: widget.pulses,
                                now: widget.now ?? DateTime.now().toUtc(),
                                reducedMotion: disableAnimations,
                                pulseValue: pulseValue,
                                highlightEdgeId: widget.highlightEdgeId,
                              ),
                            ),
                            for (final node in projected)
                              Positioned(
                                left: node.center.dx - 9,
                                top: node.center.dy - 9,
                                child: IgnorePointer(
                                  child: NeuronIcon(
                                    kind: NeuronIconKind.fromKey(
                                      node.node.iconKey,
                                    ),
                                    size: 18,
                                  ),
                                ),
                              ),
                            for (final edge in projectedEdges)
                              IgnorePointer(
                                key: Key('graph_edge_${edge.edge.id}'),
                              ),
                          ],
                        ),
                      ),
                    ),
                  ),
                  Positioned(
                    right: 8,
                    top: 8,
                    child: IconButton(
                      tooltip: 'Fit graph',
                      icon: const Icon(Icons.center_focus_strong_outlined),
                      onPressed: () {
                        _viewport.value = Matrix4.identity();
                        setState(() {
                          _rotationX = -0.18;
                          _rotationY = 0.42;
                        });
                      },
                    ),
                  ),
                ],
              );
            },
          );
        },
      ),
    );
  }
}
