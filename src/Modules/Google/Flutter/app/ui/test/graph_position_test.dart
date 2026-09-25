import 'package:digitalbrain_ui/src/components/graph/graph_geometry.dart';
import 'package:digitalbrain_ui/src/components/graph/graph_models.dart';
import 'package:digitalbrain_ui/src/components/graph/spatial_layout.dart';
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
    expect(
      expanded.singleWhere((e) => e.node.id == 'caller').center,
      initial.single.center,
    );
  });

  test('spatial layout is stable and not constrained to an orbital shell', () {
    final first = spatialLayoutGraph(const [
      GraphNode(id: 'a', label: 'A', cluster: 'alpha'),
      GraphNode(id: 'b', label: 'B', cluster: 'beta'),
    ]);
    final expanded = spatialLayoutGraph(const [
      GraphNode(id: 'a', label: 'A', cluster: 'alpha'),
      GraphNode(id: 'b', label: 'B', cluster: 'beta'),
      GraphNode(id: 'c', label: 'C', cluster: 'alpha'),
    ]);
    expect(expanded['a'], first['a']);
    expect(expanded['b'], first['b']);
    expect(first['a']!.x, isNot(first['b']!.x));
  });
}
