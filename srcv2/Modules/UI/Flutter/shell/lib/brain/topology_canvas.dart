import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:flutter/material.dart';

import 'topology_graph.dart';
import 'topology_selection.dart';

final class BrainTopologyCanvas extends StatelessWidget {
  const BrainTopologyCanvas({
    super.key,
    required this.topology,
    required this.onSelected,
    this.pulse,
    this.graphChange,
  });

  final BrainTopologySnapshot topology;
  final ChatTurnEvent? pulse;
  final GraphChangeEvent? graphChange;
  final ValueChanged<BrainTopologySelection> onSelected;

  @override
  Widget build(BuildContext context) {
    final pulseReady = hasPulseTarget(pulse);
    final localPulse = pulseReady && pulse!.caller == pulse!.neuronId;
    final edgePulse = pulseReady && !localPulse;

    void select({GraphNode? node, GraphEdge? edge}) {
      final selection =
          topologySelectionFor(topology, pulse, node: node, edge: edge);
      if (selection != null) {
        onSelected(selection);
      }
    }

    return Stack(
      key: const Key('brain_topology_canvas'),
      fit: StackFit.expand,
      children: [
        KitGraph(
          nodes: topologyGraphNodes(topology, pulse),
          edges: topologyGraphEdges(topology),
          pulse: topologyGraphPulse(pulse),
          highlightEdgeId: graphChange?.connectionId,
          onNodeTap: (node) => select(node: node),
          onEdgeTap: (edge) => select(edge: edge),
          semanticsLabel:
              'Interactive three-dimensional DigitalBrain topology. Drag to rotate; use the topology list to inspect accessible node details.',
        ),
        if (pulseReady) const IgnorePointer(key: Key('brain_pulse')),
        if (localPulse) const IgnorePointer(key: Key('brain_local_pulse')),
        if (edgePulse) const IgnorePointer(key: Key('brain_edge_pulse')),
      ],
    );
  }
}
