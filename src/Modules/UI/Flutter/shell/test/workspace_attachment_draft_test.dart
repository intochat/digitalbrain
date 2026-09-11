import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_chat.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_store.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

class _Storage implements WorkspacePersistence {
  @override
  Future<String?> read() async => null;
  @override
  Future<void> write(String value) async {}
}

void main() {
  testWidgets(
    'immediate send includes attached local draft and revision, excludes unrelated work',
    (tester) async {
      final store = WorkspaceStore(persistence: _Storage());
      await store.load();
      store.addArtifact(
        WorkspaceArtifact(
          id: 'draft',
          title: 'CRM',
          kind: 'diagram',
          content: 'rect "New unsaved contact"',
          data: {'_dirty': true, '_revision': 3},
          editorState: {'selectedId': 'contact'},
        ),
      );
      store.addArtifact(
        WorkspaceArtifact(
          id: 'hidden',
          title: 'Unattached',
          kind: 'diagram',
          content: 'do not send this',
          data: {'_dirty': true},
        ),
      );
      store.attachArtifact('draft');
      store.addArtifact(
        WorkspaceArtifact(
          id: 'live-brain-project',
          title: 'Live brain',
          kind: 'brain',
          data: {
            '_live': true,
            '_observationStale': true,
            '_observationFailure': 'Connection unavailable',
            'rootId': 'root',
            'observedAt': '2026-09-11T12:00:00Z',
            'nodes': [
              {'id': 'observed-agent', 'label': 'Actual observed agent'},
            ],
            'synapses': [],
          },
        ),
      );
      store.attachArtifact('live-brain-project');
      String? sent;
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: WorkspaceChat(
              conversation: store.currentConversation,
              store: store,
              onArtifact: (_) {},
              onAttach: () {},
              onRun:
                  ({
                    required threadId,
                    required runId,
                    parentRunId,
                    required text,
                  }) {
                    sent = text;
                    return Stream<AgentEvent>.fromIterable([
                      AgentEvent({'type': 'RUN_FINISHED'}),
                    ]);
                  },
            ),
          ),
        ),
      );
      await tester.enterText(
        find.byType(TextField),
        'Review my latest drawing',
      );
      await tester.tap(find.byTooltip('Send message'));
      await tester.pumpAndSettle();
      expect(sent, contains('New unsaved contact'));
      expect(sent, contains('local-draft-not-yet-synced'));
      expect(sent, contains('"revision":3'));
      expect(sent, contains('"selectedId":"contact"'));
      expect(sent, isNot(contains('do not send this')));
      expect(sent, contains('"syncState":"live-observation"'));
      expect(sent, contains('"currentObservation"'));
      expect(sent, contains('Actual observed agent'));
      expect(sent, contains('2026-09-11T12:00:00Z'));
      expect(sent, contains('"observationState":"stale"'));
      expect(sent, contains('"observationFailure":"Connection unavailable"'));
      expect(
        sent,
        contains('never call read_artifact or update_artifact for that ID'),
      );
      expect(
        store.currentConversation.messages.first['text'],
        'Review my latest drawing',
      );
      await tester.pumpWidget(const SizedBox.shrink());
    },
  );
}
