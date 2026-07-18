import 'package:flutter_test/flutter_test.dart';
import 'package:ino_flutter/screens/brain/brain_topology.dart';

void main() {
  group('BrainTopology', () {
    final topology = BrainTopology.load();

    test('NodeKind has only neuron and synapse', () {
      expect(NodeKind.values.map((e) => e.name).toSet(),
          equals({'neuron', 'synapse'}));
    });

    test('EdgeKind has only handler', () {
      expect(EdgeKind.values.map((e) => e.name).toSet(),
          equals({'handler'}));
    });

    test('no node id starts with "exp."', () {
      final expIds = topology.nodes.where((n) => n.id.startsWith('exp.')).toList();
      expect(expIds, isEmpty);
    });

    test('no neuron label ends in "Neuron" or "Plan"', () {
      final bad = topology.nodes
          .where((n) => n.kind == NodeKind.neuron)
          .where((n) => n.label.endsWith('Neuron') || n.label.endsWith('Plan'))
          .toList();
      expect(bad, isEmpty,
          reason: 'offending labels: ${bad.map((n) => n.label).toList()}');
    });

    test('recall collapses to a single node with id "recall"', () {
      final recallNodes =
          topology.nodes.where((n) => n.id.startsWith('recall')).toList();
      expect(recallNodes, hasLength(1));
      expect(recallNodes.single.id, equals('recall'));
      expect(recallNodes.single.label, equals('Recall'));
    });

    test('every edge points to an existing node', () {
      final ids = topology.nodes.map((n) => n.id).toSet();
      for (final e in topology.edges) {
        expect(ids, contains(e.from), reason: 'missing from: ${e.from}');
        expect(ids, contains(e.to), reason: 'missing to: ${e.to}');
      }
    });
  });
}
