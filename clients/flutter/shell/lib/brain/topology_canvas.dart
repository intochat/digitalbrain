import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

import 'topology_graph.dart';
import 'topology_painter.dart';
import 'topology_selection.dart';

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
    if (hasPulseTarget(widget.pulse)) {
      _pulse.forward();
    }
  }

  @override
  void didUpdateWidget(covariant BrainTopologyCanvas oldWidget) {
    super.didUpdateWidget(oldWidget);
    final pulseChanged =
        widget.pulse?.sequence != oldWidget.pulse?.sequence ||
        widget.pulse?.correlationId != oldWidget.pulse?.correlationId;
    final hadTarget = hasPulseTarget(oldWidget.pulse);
    final hasTarget = hasPulseTarget(widget.pulse);
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
    final pulseReady = hasPulseTarget(widget.pulse);
    final localPulse =
        pulseReady && widget.pulse!.caller == widget.pulse!.neuronId;
    final edgePulse = pulseReady && !localPulse;

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
              final projected = projectTopology(
                widget.topology,
                size,
                _rotationX,
                _rotationY,
                pulse: widget.pulse,
              );
              final pulseValue = disableAnimations ? 1.0 : _pulse.value;

              return GestureDetector(
                behavior: HitTestBehavior.opaque,
                onPanUpdate: _rotate,
                onTapUp: (details) {
                  final selected =
                      hitTestTopology(projected, details.localPosition);
                  if (selected != null) {
                    widget.onSelected(selected.selection);
                  }
                },
                child: Stack(
                  fit: StackFit.expand,
                  children: [
                    CustomPaint(
                      painter: TopologyPainter(
                        nodes: projected,
                        pulse: widget.pulse,
                        pulseValue: pulseValue,
                      ),
                    ),
                    if (pulseReady)
                      const IgnorePointer(key: Key('brain_pulse')),
                    if (localPulse)
                      const IgnorePointer(key: Key('brain_local_pulse')),
                    if (edgePulse)
                      const IgnorePointer(key: Key('brain_edge_pulse')),
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
