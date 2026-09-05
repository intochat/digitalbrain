import 'dart:math' as math;
import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';
import '../../digitalbrain_ui_kit.dart';

/// Stable module layout. Journal updates change emphasis, never the camera.
final class LumenBrainGraph extends StatefulWidget {
  const LumenBrainGraph({
    super.key,
    required this.snapshot,
    required this.onNeuron,
    required this.onSynapse,
    this.selectedId,
    this.stale = false,
    this.activeNodes = const {},
    this.activeEdges = const {},
  });
  final BrainSnapshot snapshot;
  final ValueChanged<BrainNeuron> onNeuron;
  final ValueChanged<BrainSynapse> onSynapse;
  final String? selectedId;
  final bool stale;
  final Set<String> activeNodes, activeEdges;
  @override
  State<LumenBrainGraph> createState() => _LumenBrainGraphState();
}

final class _LumenBrainGraphState extends State<LumenBrainGraph> {
  final _transform = TransformationController();
  @override
  void dispose() {
    _transform.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final assistantWorking =
        !widget.stale &&
        widget.snapshot.nodes.any(
          (node) =>
              node.id == widget.snapshot.rootId &&
              (node.status == 'Running' || node.status == 'Active'),
        );
    final modules = <String, List<BrainNeuron>>{};
    for (final node in widget.snapshot.nodes) {
      modules.putIfAbsent(node.module, () => []).add(node);
    }
    final names = modules.keys.toList()..sort();
    if (names.length >= 3 && names.remove('AI')) names.insert(1, 'AI');
    final positions = <String, Offset>{};
    final boxes = <String, Rect>{};
    final columns = math.min(3, math.max(1, names.length));
    var y = 28.0;
    for (var row = 0; row < (names.length / columns).ceil(); row++) {
      var rowHeight = 190.0;
      final rowColumns = math.min(columns, names.length - row * columns);
      final rowInset = (columns - rowColumns) * 148.0;
      for (var col = 0; col < columns; col++) {
        final index = row * columns + col;
        if (index >= names.length) break;
        final name = names[index];
        final nodes = modules[name]!..sort((a, b) => a.id.compareTo(b.id));
        final height = 64.0 + (nodes.length / 2).ceil() * 116;
        rowHeight = math.max(rowHeight, height);
        boxes[name] = Rect.fromLTWH(28 + rowInset + col * 296, y, 274, height);
        for (var n = 0; n < nodes.length; n++) {
          positions[nodes[n].id] = Offset(
            98 +
                rowInset +
                col * 296 +
                (n % 2) * 134 +
                (n == nodes.length - 1 && nodes.length.isOdd ? 67 : 0),
            y + 99 + (n ~/ 2) * 116,
          );
        }
      }
      y += rowHeight + 34;
    }
    final size = Size(columns * 296.0 + 34, math.max(260, y));
    final routes = _synapseRoutes(positions, widget.snapshot.synapses);
    return LayoutBuilder(
      builder: (context, constraints) => Stack(
        children: [
          Positioned.fill(
            child: InteractiveViewer(
              key: const Key('lumen_graph_canvas'),
              transformationController: _transform,
              minScale: 0.55,
              maxScale: 3,
              boundaryMargin: const EdgeInsets.all(180),
              child: Center(
                child: FittedBox(
                  fit: BoxFit.contain,
                  child: SizedBox(
                    width: size.width,
                    height: size.height,
                    child: Stack(
                      children: [
                        for (final entry in boxes.entries)
                          Positioned.fromRect(
                            rect: entry.value,
                            child: Container(
                              decoration: BoxDecoration(
                                color: const Color(
                                  0xffedf0e9,
                                ).withValues(alpha: 0.65),
                                borderRadius: BorderRadius.circular(26),
                                border: Border.all(
                                  color: const Color(0xffdce3d9),
                                ),
                              ),
                              padding: const EdgeInsets.fromLTRB(20, 14, 20, 0),
                              alignment: Alignment.topLeft,
                              child: Text(
                                entry.key.toUpperCase(),
                                style: const TextStyle(
                                  color: Color(0xff768477),
                                  fontSize: 10,
                                  letterSpacing: 2,
                                  fontWeight: FontWeight.w600,
                                ),
                              ),
                            ),
                          ),
                        Positioned.fill(
                          child: CustomPaint(
                            painter: _SynapsePainter(
                              routes,
                              widget.activeEdges,
                              widget.selectedId,
                            ),
                          ),
                        ),
                        for (final route in routes)
                          Positioned(
                            left: route.controlPosition.dx - 22,
                            top: route.controlPosition.dy - 22,
                            child: SizedBox.square(
                              dimension: 44,
                              child: LumenIconButton(
                                key: ValueKey('synapse_${route.edge.id}'),
                                label:
                                    'Inspect ${route.edge.kind} synapse ${route.edge.signalType}, from ${route.edge.sourceId} to ${route.edge.targetId}',
                                selected: widget.selectedId == route.edge.id,
                                onPressed: () => widget.onSynapse(route.edge),
                                icon: const Icon(
                                  Icons.arrow_outward_rounded,
                                  size: 16,
                                ),
                              ),
                            ),
                          ),
                        for (final node in widget.snapshot.nodes)
                          Positioned(
                            left: positions[node.id]!.dx - 58,
                            top: positions[node.id]!.dy - 42,
                            child: _NeuronTile(
                              node: node,
                              selected: widget.selectedId == node.id,
                              active:
                                  widget.activeNodes.contains(node.id) ||
                                  (brainNeuronIcon(node) ==
                                          NeuronIconKind.assistant &&
                                      assistantWorking),
                              stale: widget.stale,
                              onTap: () => widget.onNeuron(node),
                            ),
                          ),
                      ],
                    ),
                  ),
                ),
              ),
            ),
          ),
          Positioned(
            right: 12,
            bottom: 8,
            child: IconButton.filledTonal(
              tooltip: 'Reset graph view',
              onPressed: () => _transform.value = Matrix4.identity(),
              icon: const Icon(Icons.center_focus_strong_outlined, size: 18),
            ),
          ),
        ],
      ),
    );
  }
}

NeuronIconKind brainNeuronIcon(BrainNeuron node) {
  final type = '${node.type} ${node.module}'.toLowerCase();
  if (type.contains('assistant')) return NeuronIconKind.assistant;
  if (type.contains('worker') || type.contains('execution')) {
    return NeuronIconKind.execution;
  }
  if (type.contains('chat')) return NeuronIconKind.conversation;
  if (type.contains('session')) return NeuronIconKind.memory;
  if (type.contains('gmail') || type.contains('google')) {
    return NeuronIconKind.gmail;
  }
  if (type.contains('salesforce')) return NeuronIconKind.salesforce;
  if (type.contains('aspire')) return NeuronIconKind.aspire;
  if (type.contains('search')) return NeuronIconKind.search;
  if (type.contains('repository') || type.contains('behavior')) {
    return NeuronIconKind.repository;
  }
  if (type.contains('timer') || type.contains('time')) {
    return NeuronIconKind.clock;
  }
  if (type.contains('document') || type.contains('context')) {
    return NeuronIconKind.document;
  }
  return NeuronIconKind.generic;
}

final class _NeuronTile extends StatelessWidget {
  const _NeuronTile({
    required this.node,
    required this.selected,
    required this.active,
    required this.stale,
    required this.onTap,
  });
  final BrainNeuron node;
  final bool selected, active, stale;
  final VoidCallback onTap;
  @override
  Widget build(BuildContext context) {
    final assistant = brainNeuronIcon(node) == NeuronIconKind.assistant;
    return Semantics(
      button: true,
      label:
          '${node.label}, ${node.module}, ${assistant && active ? 'Working' : node.status}',
      child: InkWell(
        key: ValueKey('neuron_${node.id}'),
        onTap: onTap,
        borderRadius: BorderRadius.circular(18),
        child: SizedBox(
          width: 116,
          child: Column(
            children: [
              AnimatedContainer(
                duration: const Duration(milliseconds: 250),
                width: 64,
                height: 64,
                decoration: BoxDecoration(
                  color: Colors.white,
                  borderRadius: BorderRadius.circular(19),
                  border: Border.all(
                    width: selected || active ? 2 : 1,
                    color: selected || active
                        ? const Color(0xff397b63)
                        : const Color(0xffdce3d9),
                  ),
                  boxShadow: [
                    BoxShadow(
                      color: const Color(
                        0xff4c6b4b,
                      ).withValues(alpha: active ? .18 : .06),
                      blurRadius: active ? 24 : 12,
                      offset: const Offset(0, 5),
                    ),
                  ],
                ),
                child: Center(
                  child: assistant
                      ? InoPresence(
                          size: 58,
                          state: stale
                              ? InoPresenceState.disconnected
                              : active || node.status == 'Running'
                              ? InoPresenceState.working
                              : node.status == 'Failed'
                              ? InoPresenceState.attention
                              : InoPresenceState.idle,
                        )
                      : NeuronIcon(kind: brainNeuronIcon(node), size: 32),
                ),
              ),
              const SizedBox(height: 8),
              Text(
                assistant ? 'Ino' : node.label,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: const TextStyle(
                  fontSize: 12,
                  color: Color(0xff263934),
                  fontWeight: FontWeight.w600,
                ),
              ),
              Text(
                active ? (assistant ? 'Working' : 'Just active') : node.name,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: const TextStyle(fontSize: 9, color: Color(0xff75847b)),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

final class _SynapseRoute {
  const _SynapseRoute(this.edge, this.path, this.controlPosition);
  final BrainSynapse edge;
  final Path path;
  final Offset controlPosition;
}

// The drawing and its inspector button share one route. Group both directions
// together so parallel subscriptions remain individually reachable.
List<_SynapseRoute> _synapseRoutes(
  Map<String, Offset> positions,
  List<BrainSynapse> edges,
) {
  final groups = <(String, String), List<BrainSynapse>>{};
  for (final edge in edges) {
    if (!positions.containsKey(edge.sourceId) ||
        !positions.containsKey(edge.targetId)) {
      continue;
    }
    final pair = edge.sourceId.compareTo(edge.targetId) <= 0
        ? (edge.sourceId, edge.targetId)
        : (edge.targetId, edge.sourceId);
    groups.putIfAbsent(pair, () => []).add(edge);
  }
  final routes = <_SynapseRoute>[];
  for (final group in groups.entries) {
    final siblings = group.value..sort((a, b) => a.id.compareTo(b.id));
    final canonical = positions[group.key.$2]! - positions[group.key.$1]!;
    for (var i = 0; i < siblings.length; i++) {
      final edge = siblings[i];
      final from = positions[edge.sourceId]!, to = positions[edge.targetId]!;
      final delta = to - from;
      final path = Path();
      late final Offset controlPosition;
      if (delta.distance < 1) {
        final reach = 86.0 + i * 62;
        path.moveTo(from.dx + 30, from.dy);
        path.cubicTo(
          from.dx + reach,
          from.dy - reach,
          from.dx - reach,
          from.dy - reach,
          from.dx - 30,
          from.dy,
        );
        controlPosition = Offset(from.dx, from.dy - reach * .75);
      } else {
        final a = from + delta / delta.distance * 36;
        final b = to - delta / delta.distance * 36;
        final normal = Offset(-canonical.dy, canonical.dx) / canonical.distance;
        final bend = (i - (siblings.length - 1) / 2) * 52;
        final midpoint = (a + b) / 2;
        final control = midpoint + normal * bend * 2;
        controlPosition = midpoint + normal * bend;
        path.moveTo(a.dx, a.dy);
        path.quadraticBezierTo(control.dx, control.dy, b.dx, b.dy);
      }
      routes.add(_SynapseRoute(edge, path, controlPosition));
    }
  }
  return routes;
}

final class _SynapsePainter extends CustomPainter {
  _SynapsePainter(this.routes, this.active, this.selected);
  final List<_SynapseRoute> routes;
  final Set<String> active;
  final String? selected;
  @override
  void paint(Canvas canvas, Size size) {
    for (final route in routes) {
      final edge = route.edge;
      final emphasis = active.contains(edge.id) || edge.id == selected;
      final paint = Paint()
        ..color = emphasis ? const Color(0xff397b63) : const Color(0xffb3c6b5)
        ..strokeWidth = emphasis ? 2.5 : 1.5
        ..style = PaintingStyle.stroke;
      final metric = route.path.computeMetrics().first;
      if (edge.kind == 'Learned') {
        for (double distance = 0; distance < metric.length; distance += 12) {
          canvas.drawPath(
            metric.extractPath(distance, math.min(metric.length, distance + 6)),
            paint,
          );
        }
      } else {
        canvas.drawPath(route.path, paint);
      }
      final tangent = metric.getTangentForOffset(metric.length)!;
      final b = tangent.position;
      final angle = math.atan2(tangent.vector.dy, tangent.vector.dx);
      final arrow = Path()
        ..moveTo(b.dx, b.dy)
        ..lineTo(
          b.dx - 9 * math.cos(angle - .5),
          b.dy - 9 * math.sin(angle - .5),
        )
        ..moveTo(b.dx, b.dy)
        ..lineTo(
          b.dx - 9 * math.cos(angle + .5),
          b.dy - 9 * math.sin(angle + .5),
        );
      canvas.drawPath(arrow, paint);
    }
  }

  @override
  bool shouldRepaint(covariant _SynapsePainter oldDelegate) => true;
}
