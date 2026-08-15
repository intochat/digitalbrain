import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets('KitGraph renders nodes with edge markers', (tester) async {
    GraphEdge? tapped;
    await tester.pumpWidget(
      MaterialApp(
        home: SizedBox(
          width: 600,
          height: 400,
          child: KitGraph(
            nodes: const [
              GraphNode(id: 'a', label: 'A', kind: GraphNodeKind.hub),
              GraphNode(id: 'b', label: 'B'),
            ],
            edges: const [
              GraphEdge(id: 'a-to-b', sourceId: 'a', targetId: 'b'),
            ],
            onEdgeTap: (edge) => tapped = edge,
          ),
        ),
      ),
    );

    expect(find.byKey(const Key('kit_graph')), findsOneWidget);
    expect(find.byKey(const Key('graph_edge_a-to-b')), findsOneWidget);
    expect(tapped, isNull);
  });

  testWidgets('KitGraph drops edges whose endpoints are unknown', (
    tester,
  ) async {
    await tester.pumpWidget(
      const MaterialApp(
        home: SizedBox(
          width: 600,
          height: 400,
          child: KitGraph(
            nodes: [GraphNode(id: 'a', label: 'A')],
            edges: [
              GraphEdge(id: 'dangling', sourceId: 'a', targetId: 'missing'),
            ],
          ),
        ),
      ),
    );

    expect(find.byKey(const Key('graph_edge_dangling')), findsNothing);
  });
}
