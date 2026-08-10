import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';

import 'topology_selection.dart';

bool hasPulseTarget(ChatTurnEvent? pulse) => pulse != null;

List<BrainNeuron> displayedNeurons(
  BrainTopologySnapshot topology,
  ChatTurnEvent? pulse,
) {
  final neurons = List<BrainNeuron>.of(topology.neurons);
  void ensure(String id) {
    if (id.isEmpty || neurons.any((neuron) => neuron.id == id)) {
      return;
    }
    neurons.add(optimisticNeuron(id));
  }

  if (pulse != null) {
    ensure(pulse.neuronId);
    ensure(pulse.caller);
  }

  for (final connection in topology.connections) {
    ensure(connection.source);
    ensure(connection.target);
  }

  return neurons;
}

BrainNeuron optimisticNeuron(String id) {
  final separator = id.indexOf(':');
  if (separator <= 0 || separator == id.length - 1) {
    return BrainNeuron(
      id: id,
      grainType: id,
      identity: id,
      placement: 'pending',
    );
  }

  return BrainNeuron(
    id: id,
    grainType: id.substring(0, separator),
    identity: id.substring(separator + 1),
    placement: 'pending',
  );
}

List<GraphNode> topologyGraphNodes(
  BrainTopologySnapshot topology,
  ChatTurnEvent? pulse,
) => [
  for (final module in topology.modules)
    GraphNode(
      id: module.id,
      label: brainModuleLabel(module),
      kind: GraphNodeKind.hub,
    ),
  for (final neuron in displayedNeurons(topology, pulse))
    GraphNode(
      id: neuron.id,
      label: neuron.grainType,
      dimmed: neuron.placement == 'pending',
    ),
];

List<GraphEdge> topologyGraphEdges(BrainTopologySnapshot topology) => [
  for (final connection in topology.connections)
    GraphEdge(
      id: connection.connectionId,
      sourceId: connection.source,
      targetId: connection.target,
      decorated: connection.transform != null,
    ),
];

GraphPulse? topologyGraphPulse(ChatTurnEvent? pulse) => pulse == null
    ? null
    : GraphPulse(
        fromId: pulse.caller,
        toId: pulse.neuronId,
        signature: '${pulse.sequence}:${pulse.correlationId}',
      );

BrainTopologySelection? topologySelectionFor(
  BrainTopologySnapshot topology,
  ChatTurnEvent? pulse, {
  GraphNode? node,
  GraphEdge? edge,
}) {
  if (node != null) {
    for (final module in topology.modules) {
      if (module.id == node.id) {
        return BrainModuleSelection(module);
      }
    }
    for (final neuron in displayedNeurons(topology, pulse)) {
      if (neuron.id == node.id) {
        return BrainNeuronSelection(neuron);
      }
    }
    return null;
  }

  if (edge != null) {
    for (final connection in topology.connections) {
      if (connection.connectionId == edge.id) {
        return BrainConnectionSelection(connection);
      }
    }
  }

  return null;
}
