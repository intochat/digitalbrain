import 'package:digitalbrain_flutter_shell/workspace/workspace_desktop.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_store.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'workspace_remote_controller_test.dart' show MemoryPersistence;

class CountingPersistence extends MemoryPersistence {
  var writes = 0;
  @override
  Future<void> write(String next) async {
    writes++;
    await super.write(next);
  }
}

void main() {
  testWidgets('dragging a workspace window persists once, not every move', (
    tester,
  ) async {
    final persistence = CountingPersistence();
    final store = WorkspaceStore(persistence: persistence);
    store.addArtifact(
      WorkspaceArtifact(id: 'note', title: 'Notes', kind: 'document'),
    );
    store.openArtifact('note', placement: 'floating');
    await tester.pumpAndSettle();
    persistence.writes = 0;
    var editorBuilds = 0;
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: SizedBox(
            width: 1200,
            height: 800,
            child: WorkspaceDesktop(
              store: store,
              editorBuilder: (artifact) {
                editorBuilds++;
                return Text(artifact.title);
              },
            ),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    final buildsBeforeDrag = editorBuilds;
    expect(buildsBeforeDrag, greaterThan(0));
    await tester.drag(
      find.byKey(const ValueKey('window-drag-note')),
      const Offset(48, 24),
    );
    await tester.pumpAndSettle();
    expect(persistence.writes, 1);
    expect(editorBuilds, lessThanOrEqualTo(buildsBeforeDrag + 1));
    final bounds = store.currentProject.presentation.windowBounds['note']!;
    expect(bounds[0], isNot(0));
    store.dispose();
  });
}
