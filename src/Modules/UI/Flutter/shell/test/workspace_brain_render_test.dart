import 'package:digitalbrain_flutter_shell/workspace/workspace_app.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_store.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:markdraw/markdraw.dart' as markdraw;

class _Memory implements WorkspacePersistence {
  @override
  Future<String?> read() async => null;
  @override
  Future<void> write(String value) async {}
}

void main() {
  testWidgets(
    'New work Drawing sends serialized empty Markdraw source and opens editor',
    (tester) async {
      await tester.binding.setSurfaceSize(const Size(1440, 1000));
      addTearDown(() => tester.binding.setSurfaceSize(null));
      final store = WorkspaceStore(persistence: _Memory());
      Map<String, dynamic>? submitted;
      await tester.pumpWidget(
        WorkspaceApp(
          store: store,
          onCreateArtifact:
              ({required kind, required title, required content}) async {
                submitted = content;
                return {
                  'kind': kind,
                  'title': title,
                  'id': 'drawing',
                  'revision': 1,
                  'content': content,
                };
              },
        ),
      );
      await tester.pumpAndSettle();
      await tester.tap(find.text('My project'));
      await tester.pumpAndSettle();
      await tester.tap(find.byTooltip('New work'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Drawing'));
      await tester.pumpAndSettle();
      expect(submitted?['source'], isA<String>());
      final source = submitted!['source'] as String;
      expect(source, isNotEmpty);
      final controller = markdraw.MarkdrawController();
      controller.loadFromContent(source, 'drawing.markdraw');
      expect(controller.serializeScene(), source);
      controller.dispose();
      expect(find.byType(markdraw.MarkdrawEditor), findsOneWidget);
      expect(store.currentProject.artifacts.single.content, source);
      expect(tester.takeException(), isNull);
    },
  );
  testWidgets(
    'production workspace renders arbitrary two neuron scenario and clickable synapse',
    (tester) async {
      await tester.binding.setSurfaceSize(const Size(1440, 1000));
      addTearDown(() => tester.binding.setSurfaceSize(null));
      final store = WorkspaceStore(persistence: _Memory());
      await store.load();
      store.addArtifact(
        WorkspaceArtifact(
          id: 'scenario',
          title: 'Research approval',
          kind: 'brain',
          data: {
            'rootId': 'research-agent',
            'observedAt': '2026-09-11T10:00:00Z',
            'nodes': [
              for (final id in ['research-agent', 'human-review'])
                {
                  'id': id,
                  'type': 'agent',
                  'name': id,
                  'label': id,
                  'module': 'draft',
                  'status': 'Idle',
                },
            ],
            'synapses': [
              {
                'id': 'handoff',
                'sourceId': 'research-agent',
                'targetId': 'human-review',
                'signalType': 'research findings',
                'kind': 'handoff',
              },
            ],
          },
        ),
      );
      store.openArtifact('scenario');
      await tester.pumpWidget(WorkspaceApp(store: store));
      await tester.pumpAndSettle();
      await tester.tap(find.text('My project'));
      await tester.pumpAndSettle();
      expect(tester.takeException(), isNull);
      final node = find.byKey(const ValueKey('neuron_research-agent'));
      final edge = find.byKey(const ValueKey('synapse_handoff'));
      expect(node, findsOneWidget);
      expect(edge, findsOneWidget);
      // Small graphs fit the actual occupied area, leaving labels legible.
      expect(tester.getRect(node).width, greaterThan(100));
      await tester.tap(edge);
      await tester.pumpAndSettle();
      expect(
        store.currentProject.artifacts.single.viewState['selectedId'],
        'handoff',
      );
      expect(tester.takeException(), isNull);
    },
  );
}
