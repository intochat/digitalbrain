import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

const _nodes = [
  GraphNode(id: 'brain', label: 'BRAIN', kind: GraphNodeKind.hub),
  GraphNode(id: 'excel', label: 'excel'),
  GraphNode(id: 'sheet', label: 'budget.xlsx'),
];
const _edges = [
  GraphEdge(id: 'e1', sourceId: 'brain', targetId: 'excel'),
  GraphEdge(id: 'e2', sourceId: 'excel', targetId: 'sheet'),
];

Future<KitGraphController> mount(WidgetTester tester) async {
  final controller = KitGraphController(nodes: _nodes, edges: _edges);
  await tester.pumpWidget(
    MaterialApp(
      home: Scaffold(body: KitGraphNavigator(controller: controller)),
    ),
  );
  return controller;
}

void main() {
  testWidgets('prompts when nothing is selected', (tester) async {
    await mount(tester);
    expect(find.byKey(const Key('kit_graph_nav_empty')), findsOneWidget);
  });

  testWidgets('shows the breadcrumb for the selection', (tester) async {
    final c = await mount(tester);
    c.focus('sheet');
    await tester.pump();

    expect(find.byKey(const Key('kit_graph_crumb_brain')), findsOneWidget);
    expect(find.byKey(const Key('kit_graph_crumb_excel')), findsOneWidget);
    expect(find.byKey(const Key('kit_graph_crumb_sheet')), findsOneWidget);
  });

  testWidgets('tapping a crumb navigates to it', (tester) async {
    final c = await mount(tester);
    c.focus('sheet');
    await tester.pump();

    await tester.tap(find.byKey(const Key('kit_graph_crumb_excel')));
    await tester.pump();
    expect(c.selected, 'excel');
  });

  testWidgets('back is disabled until there is history', (tester) async {
    final c = await mount(tester);
    final back = find.byKey(const Key('kit_graph_nav_back'));
    expect(tester.widget<IconButton>(back).onPressed, isNull);

    c
      ..focus('excel')
      ..focus('sheet');
    await tester.pump();
    expect(tester.widget<IconButton>(back).onPressed, isNotNull);

    await tester.tap(back);
    await tester.pump();
    expect(c.selected, 'excel');
  });

  testWidgets('neighbour chips navigate', (tester) async {
    final c = await mount(tester);
    c.focus('excel');
    await tester.pump();

    expect(find.byKey(const Key('kit_graph_neighbour_brain')), findsOneWidget);
    await tester.tap(find.byKey(const Key('kit_graph_neighbour_sheet')));
    await tester.pump();
    expect(c.selected, 'sheet');
  });
}
