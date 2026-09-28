import 'package:digitalbrain_flutter_shell/workspace/workspace_store.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_chat.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_voice.dart';
import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'workspace_remote_controller_test.dart' show MemoryPersistence;

void main() {
  test(
    'server windows stay closed on a device without saved preferences',
    () async {
      final store = WorkspaceStore(persistence: MemoryPersistence());
      await store.load();
      store.reconcileWorkspace(
        store.currentProject,
        WorkspaceSnapshot(
          revision: 3,
          windows: [
            WorkspaceWindow(
              id: 'old-table',
              title: 'Saved table',
              kind: 'table',
              neuronId: 'old-table',
              isOpen: true,
            ),
          ],
        ),
      );
      expect(store.currentProject.presentation.openArtifactIds, isEmpty);
      expect(store.currentProject.artifacts, hasLength(1));
      store.dispose();
    },
  );
  test('restart keeps saved work but starts with no open windows', () async {
    final persistence = MemoryPersistence();
    final original = WorkspaceStore(persistence: persistence);
    original.launchLocalApp('images');
    original.currentConversation.draft = 'Keep my draft';
    await original.save();
    original.dispose();
    final restored = WorkspaceStore(persistence: persistence);
    await restored.load();
    expect(restored.currentProject.presentation.openArtifactIds, isEmpty);
    expect(restored.currentProject.artifacts, hasLength(1));
    expect(restored.currentConversation.draft, 'Keep my draft');
    restored.dispose();
  });

  testWidgets('assistant composer exposes voice recording', (tester) async {
    final store = WorkspaceStore(persistence: MemoryPersistence());
    store.currentConversation.draft = 'Existing draft';
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: WorkspaceChat(
            conversation: store.currentConversation,
            store: store,
            onArtifact: (_) {},
            onAttach: () {},
          ),
        ),
      ),
    );
    expect(find.byTooltip('Record a voice draft'), findsOneWidget);
    tester
        .widget<WorkspaceVoiceButton>(find.byType(WorkspaceVoiceButton))
        .onDraft!('spoken words');
    await tester.pump();
    expect(store.currentConversation.draft, 'Existing draft spoken words');
    expect(store.currentConversation.messages, isEmpty);
    await tester.pumpWidget(const SizedBox());
    store.dispose();
  });

  test(
    'startup snapshot stays quiet until saved work is explicitly reopened',
    () async {
      final persistence = MemoryPersistence();
      final original = WorkspaceStore(persistence: persistence);
      await original.save();
      original.dispose();
      final store = WorkspaceStore(persistence: persistence);
      await store.load();
      WorkspaceSnapshot snapshot(int revision, bool open) => WorkspaceSnapshot(
        revision: revision,
        windows: [
          WorkspaceWindow(
            id: 'table',
            title: 'Saved table',
            kind: 'table',
            neuronId: 'table',
            isOpen: open,
          ),
        ],
      );
      store.reconcileWorkspace(store.currentProject, snapshot(1, true));
      store.reconcileWorkspace(store.currentProject, snapshot(2, true));
      expect(store.currentProject.presentation.openArtifactIds, isEmpty);
      expect(store.currentProject.artifacts.single.title, 'Saved table');
      store.onRemoteWindowAction = (_, id, open) =>
          store.reconcileWorkspace(store.currentProject, snapshot(3, open));
      store.openArtifact('table');
      expect(store.currentProject.presentation.openArtifactIds, ['table']);
      store.dispose();
    },
  );
}
