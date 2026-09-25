import 'package:digitalbrain_ui/src/components/graph/graph_geometry.dart';
import 'package:digitalbrain_ui/src/components/graph/graph_models.dart';
import 'package:digitalbrain_ui/src/components/graph/spatial_layout.dart';
import 'package:flutter/widgets.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('graph fits connected neurons across the available canvas', () {
    final projected = projectGraphNodes(
      const [
        GraphNode(
          id: 'external',
          label: 'External',
          position: GraphPoint(2.1, -1.2, 0),
        ),
        GraphNode(
          id: 'agent',
          label: 'Agent',
          position: GraphPoint(3.8, .4, 0),
        ),
        GraphNode(
          id: 'table',
          label: 'Table',
          position: GraphPoint(.1, -1.3, 0),
        ),
        GraphNode(
          id: 'workspace',
          label: 'Workspace',
          position: GraphPoint(.3, -2.5, 0),
        ),
        GraphNode(
          id: 'usage',
          label: 'Usage',
          position: GraphPoint(.4, 4.6, 0),
        ),
      ],
      const Size(600, 300),
      0,
      0,
    );
    final xs = projected.map((node) => node.center.dx);
    expect(
      xs.reduce((a, b) => a > b ? a : b) - xs.reduce((a, b) => a < b ? a : b),
      greaterThan(250),
    );
  });

  test('fitted graph positions are independent of input order', () {
    final initial = projectGraphNodes(
      const [
        GraphNode(id: 'caller', label: 'Caller'),
        GraphNode(id: 'target', label: 'Target'),
      ],
      const Size(800, 600),
      0,
      0,
    );
    final reversed = projectGraphNodes(
      const [
        GraphNode(id: 'target', label: 'Target'),
        GraphNode(id: 'caller', label: 'Caller'),
      ],
      const Size(800, 600),
      0,
      0,
    );
    expect(
      reversed.singleWhere((e) => e.node.id == 'caller').center,
      initial.singleWhere((e) => e.node.id == 'caller').center,
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
