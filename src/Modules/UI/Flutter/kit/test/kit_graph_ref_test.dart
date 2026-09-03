import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:flutter/material.dart';
import 'package:flutter_chat_core/flutter_chat_core.dart';
import 'package:flutter_test/flutter_test.dart';

const _offer = ChatGraphOffer(
  title: 'Module deps',
  nodes: [
    ChatGraphNode(id: 'brain', label: 'BRAIN', kind: 'hub'),
    ChatGraphNode(id: 'excel', label: 'EXCEL', cluster: 'modules'),
  ],
  edges: [
    ChatGraphEdge(id: 'brain-excel', sourceId: 'brain', targetId: 'excel'),
  ],
);

/// No GL in a widget test, so the loaded case renders against a stub scene.
final class _StubScene implements GraphScene {
  @override
  Future<void> load(
    List<GraphNode> nodes,
    List<GraphEdge> edges,
    Map<String, GraphPoint> layout,
  ) async {}

  @override
  void applyCamera(GraphCameraState state) {}

  @override
  String? pick(Offset local) => null;

  @override
  Offset? project(String nodeId) => null;

  @override
  Widget build(BuildContext context) => const SizedBox.expand();

  @override
  void dispose() {}
}

Future<void> pumpRef(WidgetTester tester, {KitGraphRefReader? reader}) async {
  const part = KitGraphRefPart(name: 'graph-abc', caption: 'Module deps');
  final message = CustomMessage(
    id: 'm1',
    authorId: 'assistant',
    createdAt: DateTime.utc(2026, 9, 3),
    metadata: part.toMetadata(),
  );

  await tester.pumpWidget(
    MaterialApp(
      home: Scaffold(
        body: SizedBox(
          height: 500,
          child: Builder(
            builder: (context) => KitChatBuilders.customMessageBuilder(
              context,
              message,
              0,
              isSentByMe: false,
              onReadGraph: reader,
              graphSceneFactory: _StubScene.new,
            ),
          ),
        ),
      ),
    ),
  );
}

void main() {
  testWidgets('falls back to the caption with no reader', (tester) async {
    await pumpRef(tester);

    expect(
      find.byKey(const Key('kit_graph_ref_offline_graph-abc')),
      findsOneWidget,
    );
    expect(find.text('Module deps'), findsOneWidget);
  });

  testWidgets('shows a spinner while the entity loads', (tester) async {
    // Never pumpAndSettle around a mounted KitGraphView: its render Ticker runs
    // continuously by design, so the frame queue never drains. Drive the
    // pending read explicitly instead.
    final pending = Completer<ChatGraphOffer?>();
    await pumpRef(tester, reader: (_) => pending.future);

    expect(find.byKey(const Key('kit_graph_ref_loading')), findsOneWidget);

    pending.complete(_offer);
    await tester.pump();

    expect(find.byKey(const Key('kit_graph_ref_loading')), findsNothing);
    expect(find.byType(KitGraphView), findsOneWidget);
  });

  testWidgets('falls back to the caption when the entity is gone', (
    tester,
  ) async {
    await pumpRef(tester, reader: (_) async => null);
    await tester.pump();

    expect(
      find.byKey(const Key('kit_graph_ref_missing_graph-abc')),
      findsOneWidget,
    );
  });

  testWidgets('renders the graph and its navigator when loaded', (
    tester,
  ) async {
    await pumpRef(tester, reader: (_) async => _offer);
    await tester.pump();

    expect(find.byType(KitGraphView), findsOneWidget);
    expect(find.byType(KitGraphNavigator), findsOneWidget);
    // The navigator starts unselected, prompting the reader to explore.
    expect(find.byKey(const Key('kit_graph_nav_empty')), findsOneWidget);
  });
}
