import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:digitalbrain_flutter_shell/workspace/workspace_remote_controller.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_store.dart';

class MemoryPersistence implements WorkspacePersistence {
  String? value;
  @override
  Future<String?> read() async => value;
  @override
  Future<void> write(String next) async {
    value = next;
  }
}

void main() {
  test(
    'reload keeps device layout while remote membership remains authoritative',
    () async {
      final persistence = MemoryPersistence();
      final store = WorkspaceStore(persistence: persistence);
      final project = store.currentProject;
      WorkspaceSnapshot snapshot(int revision, bool open) => WorkspaceSnapshot(
        revision: revision,
        windows: [
          WorkspaceWindow(
            id: 'result',
            title: 'Leads',
            kind: 'table',
            neuronId: 'table',
            isOpen: open,
          ),
        ],
      );
      store.reconcileWorkspace(project, snapshot(1, true));
      project.presentation.windowBounds['result'] = [100, 80, 700, 400];
      project.presentation.windowModes['result'] = 'right';
      await store.save();
      store.dispose();
      final restored = WorkspaceStore(persistence: persistence);
      await restored.load();
      final layout = restored.currentProject.presentation;
      expect(restored.currentProject.artifacts.single.remoteManaged, isTrue);
      restored.reconcileWorkspace(restored.currentProject, snapshot(2, false));
      expect(layout.openArtifactIds, isEmpty);
      restored.reconcileWorkspace(restored.currentProject, snapshot(1, true));
      expect(layout.openArtifactIds, isEmpty);
      restored.reconcileWorkspace(restored.currentProject, snapshot(3, true));
      expect(layout.openArtifactIds, ['result']);
      expect(layout.windowBounds['result'], [100, 80, 700, 400]);
      expect(layout.windowModes['result'], 'right');
      expect(restored.currentProject.artifacts, hasLength(1));
      restored.dispose();
    },
  );
  test('remote snapshots preserve local windows and never change workspace selection', () async {
    final store = WorkspaceStore(persistence: MemoryPersistence());
    final project = store.currentProject;
    project.artifacts.add(
      WorkspaceArtifact(id: 'local', title: 'Notes', kind: 'document'),
    );
    project.presentation.openArtifactIds.add('local');
    final changes = StreamController<WorkspaceRevision>.broadcast();
    final read = Completer<WorkspaceSnapshot>();
    final controller = WorkspaceRemoteController(
      read: (_) => read.future,
      watch: (_) => changes.stream,
      apply: (snapshot) => store.reconcileWorkspace(project, snapshot),
    );
    controller.start(project.id);
    expect(changes.hasListener, isTrue);
    final selected = store.createProject('Other workspace').id;
    read.complete(
      WorkspaceSnapshot(
        revision: 1,
        windows: [
          const WorkspaceWindow(
            id: 'result',
            title: 'Leads',
            kind: 'table',
            neuronId: 'table',
            isOpen: true,
          ),
        ],
      ),
    );
    await Future<void>.delayed(Duration.zero);
    expect(
      project.presentation.openArtifactIds,
      containsAll(['local', 'result']),
    );
    changes.add(WorkspaceRevision(project.id, 1));
    await Future<void>.delayed(Duration.zero);
    expect(project.artifacts.where((a) => a.id == 'result').length, 1);
    expect(store.selectedProjectId, selected);
    store.reconcileWorkspace(
      project,
      WorkspaceSnapshot(
        revision: 2,
        windows: [
          const WorkspaceWindow(
            id: 'result',
            title: 'Leads',
            kind: 'table',
            neuronId: 'table',
            isOpen: false,
          ),
        ],
      ),
    );
    expect(project.presentation.openArtifactIds, ['local']);
    expect(project.artifacts.last.remoteManaged, isTrue);
    controller.dispose();
    await changes.close();
    store.dispose();
  });
}
