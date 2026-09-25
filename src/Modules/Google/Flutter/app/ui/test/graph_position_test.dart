import 'package:digitalbrain_ui/src/components/graph/graph_geometry.dart';
import 'package:digitalbrain_ui/src/components/graph/graph_models.dart';
import 'package:flutter/widgets.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('adding a neuron keeps existing graph positions stable', () {
    final initial = projectGraphNodes(
      const [GraphNode(id: 'caller', label: 'Caller')],
      const Size(800, 600),
      0,
      0,
    );
    final expanded = projectGraphNodes(
      const [
        GraphNode(id: 'caller', label: 'Caller'),
        GraphNode(id: 'target', label: 'Target'),
      ],
      const Size(800, 600),
      0,
      0,
    );
    expect(expanded.singleWhere((e) => e.node.id == 'caller').center,
        initial.single.center);
  });
}
