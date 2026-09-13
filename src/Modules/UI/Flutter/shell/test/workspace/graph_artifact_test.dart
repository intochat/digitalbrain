import 'package:digitalbrain_flutter_shell/workspace/graph_artifact.dart';
import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('a code_map result parses into graph nodes and edges', () {
    final data = GraphArtifactData.fromMap({
      'kind': 'graph',
      'name': 'map-1',
      'title': 'Fixture',
      'nodes': [
        {'id': 'Alpha', 'label': 'Alpha', 'kind': 'module', 'cluster': 'Alpha'},
        {'id': 'Beta', 'label': 'Beta', 'kind': 'module', 'cluster': 'Beta'},
      ],
      'edges': [
        {
          'id': 'Beta-Alpha',
          'sourceId': 'Beta',
          'targetId': 'Alpha',
          'dotted': false,
        },
      ],
    });
    expect(data.title, 'Fixture');
    expect(data.nodes.map((n) => n.id), ['Alpha', 'Beta']);
    expect(data.nodes.first.kind, GraphNodeKind.module);
    expect(data.nodes.first.cluster, 'Alpha');
    expect(data.edges.single.sourceId, 'Beta');
  });

  test('an unknown kind falls back to leaf and missing edges to empty', () {
    final data = GraphArtifactData.fromMap({
      'title': 'x',
      'nodes': [
        {'id': 'a', 'label': 'A', 'kind': 'weird'},
      ],
    });
    expect(data.nodes.single.kind, GraphNodeKind.leaf);
    expect(data.edges, isEmpty);
  });
}
