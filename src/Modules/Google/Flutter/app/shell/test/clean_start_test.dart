import 'package:digitalbrain_flutter_shell/workspace/workspace_store.dart';
import 'package:digitalbrain_ui/digitalbrain_ui.dart';

import 'dart:typed_data';

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

  testWidgets('declared voice input sends audio to its neuron', (tester) async {
    final events = <Map<String, dynamic>>[];
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: NeuronView(
            kind: 'voiceinput',
            name: 'voice',
            load: (_, _) async => {'label': 'Dictate'},
            onAction: (event) async => events.add(event),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.byTooltip('Dictate'), findsOneWidget);
    await tester
        .widget<UiVoiceInput>(find.byType(UiVoiceInput))
        .onAudio!(Uint8List.fromList([1, 2, 3]), 'audio/wav');
    await tester.pump();
    expect(events.single, {
      'kind': 'voiceinput',
      'name': 'voice',
      'value': 'AQID',
      'mimeType': 'audio/wav',
    });
    await tester.pumpWidget(const SizedBox());
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
