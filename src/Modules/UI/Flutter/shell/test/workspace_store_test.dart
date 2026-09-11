import 'dart:convert';

import 'package:digitalbrain_flutter_shell/workspace/workspace_store.dart';
import 'package:flutter_test/flutter_test.dart';

class MemoryPersistence implements WorkspacePersistence {
  String? value;
  bool fail = false;
  @override
  Future<String?> read() async => value;
  @override
  Future<void> write(String value) async {
    if (fail) throw StateError('Device full');
    await Future<void>.delayed(Duration.zero);
    this.value = value;
  }
}

void main() {
  test('opening, minimizing, closing and switching agent preserve explicit context', () async {
    final store = WorkspaceStore(persistence: MemoryPersistence());
    await store.load();
    store.addArtifact(
      WorkspaceArtifact(id: 'table', title: 'My table', kind: 'table'),
    );
    store.addArtifact(
      WorkspaceArtifact(id: 'drawing', title: 'My drawing', kind: 'drawing'),
    );
    store.attachArtifact('table');
    store.openArtifact('drawing');
    store.setAgent('salesforce');
    expect(store.currentConversation.attachedArtifactIds, {'table'});
    store.minimizeArtifact('drawing');
    store.closeArtifact('drawing');
    expect(store.currentProject.artifacts.length, 2);
    expect(store.currentConversation.attachedArtifactIds, {'table'});
    store.createConversation();
    expect(store.currentConversation.attachedArtifactIds, isEmpty);
    await store.flush();
  });

  test('round trip restores projects, transcript, editor state and layout independently', () async {
    final disk = MemoryPersistence();
    final store = WorkspaceStore(persistence: disk);
    await store.load();
    final originalProjectId = store.currentProject.id;
    final conversationId = store.currentConversation.id;
    store.addArtifact(
      WorkspaceArtifact(
        id: 'table',
        title: 'Budget',
        kind: 'table',
        data: {
          'rows': [
            ['A', 12],
          ],
        },
        editorState: {'filter': 'A'},
      ),
    );
    store.attachArtifact('table');
    store.openArtifact('table');
    store.setWindowBounds('table', [10, 20, 600, 400]);
    store.setLayout('windows');
    store.setMessages(conversationId, [
      {'role': 'user', 'text': 'Hello'},
    ]);
    store.currentConversation.threadId = 'backend-thread';
    store.settings.displayName = 'Alex';
    store.settings.theme = 'dark';
    store.createProject('Second project');
    await store.flush();
    final restored = WorkspaceStore(persistence: disk);
    await restored.load();
    expect(restored.currentProject.title, 'Second project');
    expect(restored.currentProject.artifacts, isEmpty);
    restored.selectProject(originalProjectId);
    expect(restored.currentConversation.messages.single['text'], 'Hello');
    expect(restored.currentConversation.threadId, 'backend-thread');
    expect(restored.currentConversation.attachedArtifactIds, {'table'});
    expect(restored.currentProject.artifacts.single.editorState['filter'], 'A');
    expect(restored.currentProject.presentation.windowBounds['table'], [
      10,
      20,
      600,
      400,
    ]);
    expect(restored.currentProject.presentation.layout, 'windows');
    expect(restored.settings.displayName, 'Alex');
    expect(restored.settings.theme, 'dark');
    await restored.flush();
  });

  test(
    'serialized writes retain newest changes and report save failures',
    () async {
      final disk = MemoryPersistence();
      final store = WorkspaceStore(persistence: disk);
      await store.load();
      store.setLayout('tabs');
      store.setLayout('compare');
      await store.flush();
      expect(
        jsonDecode(disk.value!)['projects'][0]['presentation']['layout'],
        'compare',
      );
      disk.fail = true;
      await store.save();
      expect(store.persistenceError, isNotNull);
      disk.fail = false;
      await store.save();
      expect(store.persistenceError, isNull);
    },
  );

  test(
    'corrupt storage is preserved on load and produces a recoverable workspace',
    () async {
      final disk = MemoryPersistence()..value = 'broken json';
      final store = WorkspaceStore(persistence: disk);
      await store.load();
      expect(store.persistenceError, isNotNull);
      expect(store.currentConversation.messages, isEmpty);
      expect(disk.value, 'broken json');
    },
  );
}
