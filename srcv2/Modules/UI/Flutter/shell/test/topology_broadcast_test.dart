import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/brain/topology_graph.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  final snapshot = BrainTopologySnapshot(
    modules: const [],
    neurons: const [
      BrainNeuron(
        id: 'chat:dev/main',
        grainType: 'chat',
        identity: 'dev/main',
        placement: 'cluster-1',
      ),
    ],
    observedAt: DateTime.utc(2026, 8, 10),
    broadcastRoutes: const [
      BrainBroadcastRoute(synapseAlias: 'ui.note', handlerGrainType: 'chat'),
      BrainBroadcastRoute(synapseAlias: 'probe.fact', handlerGrainType: 'sink'),
    ],
  );

  test('broadcast routes with a visible handler become alias nodes', () {
    final nodes = topologyGraphNodes(snapshot, null);

    expect(nodes.any((node) => node.id == 'broadcast:ui.note'), isTrue);
    // No 'sink' neuron is activated, so its alias node stays out of the view.
    expect(nodes.any((node) => node.id == 'broadcast:probe.fact'), isFalse);
  });

  test('broadcast routes draw dotted edges into their handler neurons', () {
    final edges = topologyGraphEdges(snapshot);

    final edge = edges.singleWhere(
      (edge) => edge.sourceId == 'broadcast:ui.note',
    );
    expect(edge.targetId, 'chat:dev/main');
    expect(edge.dotted, isTrue);
  });
}
