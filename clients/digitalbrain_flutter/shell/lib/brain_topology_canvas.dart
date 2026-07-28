import 'dart:math' as math;

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

import 'brain_theme.dart';

sealed class BrainTopologySelection {
  const BrainTopologySelection();
}

final class BrainModuleSelection extends BrainTopologySelection {
  const BrainModuleSelection(this.module);

  final BrainModule module;
}

final class BrainNeuronSelection extends BrainTopologySelection {
  const BrainNeuronSelection(this.neuron);

  final BrainNeuron neuron;
}

final class BrainPulseSelection extends BrainTopologySelection {
  const BrainPulseSelection(this.turn);

  final ChatTurnEvent turn;
}

final class BrainTopologyCanvas extends StatefulWidget {
  const BrainTopologyCanvas({
    super.key,
    required this.topology,
    required this.onSelected,
    this.pulse,
  });

  final BrainTopologySnapshot topology;
  final ChatTurnEvent? pulse;
  final ValueChanged<BrainTopologySelection> onSelected;

  @override
  State<BrainTopologyCanvas> createState() => _BrainTopologyCanvasState();
}

final class _BrainTopologyCanvasState extends State<BrainTopologyCanvas>
    with SingleTickerProviderStateMixin {
  late final AnimationController _pulse;
  double _rotationX = -0.18;
  double _rotationY = 0.42;

  @override
  void initState() {
    super.initState();
    _pulse = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 1100),
    );
    if (_hasPulseTarget(widget.topology, widget.pulse)) {
      _pulse.forward();
    }
  }

  @override
  void didUpdateWidget(covariant BrainTopologyCanvas oldWidget) {
    super.didUpdateWidget(oldWidget);
    final pulseChanged =
        widget.pulse?.sequence != oldWidget.pulse?.sequence ||
        widget.pulse?.correlationId != oldWidget.pulse?.correlationId;
    final hadTarget = _hasPulseTarget(oldWidget.topology, oldWidget.pulse);
    final hasTarget = _hasPulseTarget(widget.topology, widget.pulse);
    if (!hasTarget) {
      if (pulseChanged || hadTarget) {
        _pulse.reset();
      }
    } else if (pulseChanged || !hadTarget) {
      _pulse.forward(from: 0);
    }
  }

  @override
  void dispose() {
    _pulse.dispose();
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
    final hasPulseTarget = _hasPulseTarget(widget.topology, widget.pulse);

    return Semantics(
      key: const Key('brain_topology_canvas'),
      label:
          'Interactive three-dimensional DigitalBrain topology. Drag to rotate; use the topology list to inspect accessible node details.',
      image: true,
      child: LayoutBuilder(
        builder: (context, constraints) {
          final size = Size(constraints.maxWidth, constraints.maxHeight);
          return AnimatedBuilder(
            animation: _pulse,
            builder: (context, _) {
              final projected = _projectTopology(
                widget.topology,
                size,
                _rotationX,
                _rotationY,
              );
              final pulseValue = disableAnimations ? 1.0 : _pulse.value;

              return GestureDetector(
                behavior: HitTestBehavior.opaque,
                onPanUpdate: _rotate,
                onTapUp: (details) {
                  final selected = _hitTest(projected, details.localPosition);
                  if (selected != null) {
                    widget.onSelected(selected.selection);
                  }
                },
                child: Stack(
                  fit: StackFit.expand,
                  children: [
                    CustomPaint(
                      painter: _TopologyPainter(
                        nodes: projected,
                        pulse: widget.pulse,
                        pulseValue: pulseValue,
                      ),
                    ),
                    if (hasPulseTarget)
                      const IgnorePointer(key: Key('brain_pulse')),
                  ],
                ),
              );
            },
          );
        },
      ),
    );
  }
}

bool _hasPulseTarget(BrainTopologySnapshot topology, ChatTurnEvent? pulse) =>
    pulse != null &&
    topology.neurons.any((neuron) => neuron.id == pulse.neuronId);

final class _GraphNode {
  const _GraphNode({
    required this.id,
    required this.label,
    required this.module,
    required this.selection,
    required this.x,
    required this.y,
    required this.z,
  });

  final String id;
  final String label;
  final bool module;
  final BrainTopologySelection selection;
  final double x;
  final double y;
  final double z;
}

final class _ProjectedNode {
  const _ProjectedNode({
    required this.node,
    required this.center,
    required this.radius,
    required this.depth,
  });

  final _GraphNode node;
  final Offset center;
  final double radius;
  final double depth;

  BrainTopologySelection get selection => node.selection;
}

List<_ProjectedNode> _projectTopology(
  BrainTopologySnapshot topology,
  Size size,
  double rotationX,
  double rotationY,
) {
  final graph = <_GraphNode>[
    ..._placeModules(topology.modules),
    ..._placeNeurons(topology.neurons),
  ];
  final base = math.min(size.width, size.height) * 0.36;
  final center = Offset(size.width * 0.5, size.height * 0.51);
  final cosY = math.cos(rotationY);
  final sinY = math.sin(rotationY);
  final cosX = math.cos(rotationX);
  final sinX = math.sin(rotationX);

  final projected = <_ProjectedNode>[];
  for (final node in graph) {
    final xY = node.x * cosY + node.z * sinY;
    final zY = -node.x * sinY + node.z * cosY;
    final yX = node.y * cosX - zY * sinX;
    final zX = node.y * sinX + zY * cosX;
    final perspective = 1.0 / (1.85 - zX * 0.36);
    final radius = (node.module ? 10.0 : 6.0) * (0.72 + perspective);

    projected.add(
      _ProjectedNode(
        node: node,
        center: Offset(
          center.dx + xY * base * perspective,
          center.dy + yX * base * perspective,
        ),
        radius: radius,
        depth: zX,
      ),
    );
  }

  projected.sort((a, b) => a.depth.compareTo(b.depth));
  return projected;
}

Iterable<_GraphNode> _placeModules(List<BrainModule> modules) sync* {
  for (var index = 0; index < modules.length; index++) {
    final position = _spherePosition(index, modules.length, 0.88, 0);
    final module = modules[index];
    yield _GraphNode(
      id: module.id,
      label: _moduleLabel(module.id),
      module: true,
      selection: BrainModuleSelection(module),
      x: position.x,
      y: position.y,
      z: position.z,
    );
  }
}

Iterable<_GraphNode> _placeNeurons(List<BrainNeuron> neurons) sync* {
  for (var index = 0; index < neurons.length; index++) {
    final position = _spherePosition(index, neurons.length, 0.62, 1.3);
    final neuron = neurons[index];
    yield _GraphNode(
      id: neuron.id,
      label: neuron.grainType,
      module: false,
      selection: BrainNeuronSelection(neuron),
      x: position.x,
      y: position.y,
      z: position.z,
    );
  }
}

({double x, double y, double z}) _spherePosition(
  int index,
  int count,
  double radius,
  double phase,
) {
  if (count <= 1) {
    return (x: 0, y: 0, z: radius);
  }
  final y = 1 - (2 * (index + 0.5) / count);
  final ring = math.sqrt(math.max(0, 1 - y * y));
  final theta = index * math.pi * (3 - math.sqrt(5)) + phase;
  return (
    x: math.cos(theta) * ring * radius,
    y: y * radius,
    z: math.sin(theta) * ring * radius,
  );
}

_ProjectedNode? _hitTest(List<_ProjectedNode> nodes, Offset position) {
  for (final node in nodes.reversed) {
    if ((node.center - position).distance <= node.radius + 8) {
      return node;
    }
  }
  return null;
}

String _moduleLabel(String id) {
  final type = id.split('.').last;
  return type.endsWith('Module')
      ? type.substring(0, type.length - 'Module'.length)
      : type;
}

final class _TopologyPainter extends CustomPainter {
  const _TopologyPainter({
    required this.nodes,
    required this.pulse,
    required this.pulseValue,
  });

  final List<_ProjectedNode> nodes;
  final ChatTurnEvent? pulse;
  final double pulseValue;

  @override
  void paint(Canvas canvas, Size size) {
    final center = Offset(size.width * 0.5, size.height * 0.51);
    final hull = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1
      ..color = BrainPalette.lineStrong.withValues(alpha: 0.55);
    final radius = math.min(size.width, size.height) * 0.34;
    canvas.drawOval(
      Rect.fromCenter(
        center: center,
        width: radius * 2.15,
        height: radius * 1.55,
      ),
      hull,
    );
    canvas.drawOval(
      Rect.fromCenter(
        center: center,
        width: radius * 1.35,
        height: radius * 2.05,
      ),
      hull..color = BrainPalette.line.withValues(alpha: 0.5),
    );

    final pulseTarget = pulse == null
        ? null
        : nodes.where((node) => node.node.id == pulse!.neuronId).firstOrNull;
    final pulseSource = pulse == null
        ? null
        : nodes.where((node) => node.node.id == pulse!.caller).firstOrNull;
    if (pulseTarget != null) {
      final wave = math.sin(pulseValue * math.pi).abs();
      final path = Path()
        ..moveTo(
          (pulseSource?.center ?? center).dx,
          (pulseSource?.center ?? center).dy,
        )
        ..quadraticBezierTo(
          center.dx,
          center.dy - radius * 0.45,
          pulseTarget.center.dx,
          pulseTarget.center.dy,
        );
      canvas.drawPath(
        path,
        Paint()
          ..style = PaintingStyle.stroke
          ..strokeWidth = 2.4
          ..color = BrainPalette.signal.withValues(alpha: 0.25 + wave * 0.7),
      );
      canvas.drawCircle(
        pulseTarget.center,
        pulseTarget.radius + 8 + wave * 16,
        Paint()
          ..style = PaintingStyle.stroke
          ..strokeWidth = 2
          ..color = BrainPalette.signal.withValues(alpha: 0.7 - wave * 0.3),
      );
    }

    for (final projected in nodes) {
      final node = projected.node;
      final color = node.module ? BrainPalette.signal : BrainPalette.owner;
      final depthAlpha = (0.5 + (projected.depth + 1) * 0.22).clamp(0.35, 1.0);
      canvas.drawCircle(
        projected.center,
        projected.radius * 1.8,
        Paint()
          ..color = color.withValues(alpha: 0.06 * depthAlpha)
          ..maskFilter = const MaskFilter.blur(BlurStyle.normal, 8),
      );
      canvas.drawCircle(
        projected.center,
        projected.radius,
        Paint()..color = color.withValues(alpha: depthAlpha),
      );
      canvas.drawCircle(
        projected.center,
        projected.radius,
        Paint()
          ..style = PaintingStyle.stroke
          ..strokeWidth = 1
          ..color = BrainPalette.textPrimary.withValues(alpha: 0.3),
      );

      if (node.module || node.id == pulse?.neuronId) {
        final text = TextPainter(
          text: TextSpan(
            text: node.label,
            style: BrainType.meta.copyWith(
              color: BrainPalette.textPrimary.withValues(alpha: depthAlpha),
            ),
          ),
          textDirection: TextDirection.ltr,
        )..layout(maxWidth: 120);
        text.paint(
          canvas,
          projected.center + Offset(-text.width / 2, projected.radius + 7),
        );
      }
    }
  }

  @override
  bool shouldRepaint(covariant _TopologyPainter oldDelegate) =>
      oldDelegate.nodes != nodes ||
      oldDelegate.pulse != pulse ||
      oldDelegate.pulseValue != pulseValue;
}
