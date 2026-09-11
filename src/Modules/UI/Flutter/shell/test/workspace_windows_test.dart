import 'package:digitalbrain_flutter_shell/workspace/workspace_store.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:flutter/material.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_desktop.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_app.dart';

class MemoryWindows implements WorkspacePersistence {
  String? data;
  @override
  Future<String?> read() async => data;
  @override
  Future<void> write(String value) async {
    data = value;
  }
}

void main() {
  testWidgets('drag previews snapping and maximizing preserves editor state', (
    tester,
  ) async {
    await tester.binding.setSurfaceSize(const Size(1000, 700));
    addTearDown(() => tester.binding.setSurfaceSize(null));
    final store = WorkspaceStore(persistence: MemoryWindows());
    store.addArtifact(
      WorkspaceArtifact(id: 'a', title: 'Table A', kind: 'document'),
    );
    store.openArtifact('a');
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: AnimatedBuilder(
            animation: store,
            builder: (context, _) => WorkspaceDesktop(
              store: store,
              editorBuilder: (a) => TextField(key: ValueKey('input-${a.id}')),
            ),
          ),
        ),
      ),
    );
    await tester.enterText(
      find.byKey(const ValueKey('input-a')),
      'Keep my editing state',
    );
    await tester.tap(find.byTooltip('Restore window'));
    await tester.pumpAndSettle();
    final gesture = await tester.startGesture(
      tester.getCenter(find.byKey(const ValueKey('window-drag-a'))),
    );
    await gesture.moveBy(const Offset(-30, 0));
    await tester.pump();
    await gesture.moveTo(const Offset(8, 100));
    await tester.pump();
    expect(find.text('Snap left'), findsOneWidget);
    await gesture.up();
    await tester.pumpAndSettle();
    expect(store.currentProject.presentation.modeOf('a'), 'left');
    expect(tester.getSize(find.byKey(const ValueKey('window-a'))).width, 500);
    await tester.tap(find.byTooltip('Maximize editor'));
    await tester.pumpAndSettle();
    expect(tester.getSize(find.byKey(const ValueKey('window-a'))).width, 1000);
    await tester.tap(find.byTooltip('Restore window'));
    await tester.pumpAndSettle();
    expect(store.currentProject.presentation.modeOf('a'), 'left');
    expect(
      tester.widget<EditableText>(find.byType(EditableText)).controller.text,
      'Keep my editing state',
    );
    store.snapWindow('a', 'floating');
    await tester.pumpAndSettle();
    final originalPosition = tester.getTopLeft(
      find.byKey(const ValueKey('window-a')),
    );
    store.addArtifact(
      WorkspaceArtifact(id: 'b', title: 'Table B', kind: 'document'),
    );
    store.openArtifact('b');
    await tester.pumpAndSettle();
    store.focusWindow('a');
    await tester.pumpAndSettle();
    expect(
      tester.getTopLeft(find.byKey(const ValueKey('window-a'))),
      originalPosition,
    );
    expect(tester.takeException(), isNull);
    await store.flush();
  });

  testWidgets(
    'minimize restores from dock and chat can collapse without losing its draft',
    (tester) async {
      await tester.binding.setSurfaceSize(const Size(1400, 900));
      addTearDown(() => tester.binding.setSurfaceSize(null));
      final store = WorkspaceStore(persistence: MemoryWindows());
      store.addArtifact(
        WorkspaceArtifact(id: 'a', title: 'My table', kind: 'document'),
      );
      store.openArtifact('a');
      await tester.pumpWidget(WorkspaceApp(store: store));
      await tester.pumpAndSettle();
      await tester.tap(find.text('My project'));
      await tester.pumpAndSettle();
      await tester.enterText(
        find.byKey(const Key('chat-message-input')),
        'Unsent question',
      );
      await tester.tap(find.byTooltip('Hide chat'));
      await tester.pumpAndSettle();
      expect(tester.takeException(), isNull);
      await tester.tap(find.byTooltip('Show chat'));
      await tester.pumpAndSettle();
      expect(
        tester
            .widget<TextField>(find.byKey(const Key('chat-message-input')))
            .controller!
            .text,
        'Unsent question',
      );
      await tester.tap(find.byTooltip('Minimize editor'));
      await tester.pumpAndSettle();
      expect(find.byKey(const ValueKey('restore-a')), findsOneWidget);
      await tester.tap(find.byKey(const ValueKey('restore-a')));
      await tester.pumpAndSettle();
      expect(find.byKey(const ValueKey('restore-a')), findsNothing);
      expect(find.byTooltip('Minimize editor'), findsOneWidget);
      expect(store.currentConversation.attachedArtifactIds, isEmpty);
      expect(tester.takeException(), isNull);
      await store.flush();
    },
  );

  test(
    'window geometry keeps title bars and resize corners in small viewports',
    () {
      final rect = workspaceWindowRect(const Size(650, 260), 'floating', [
        950,
        800,
        900,
        700,
      ], 0);
      expect(rect, const Rect.fromLTWH(0, 0, 650, 260));
      expect(
        workspaceWindowRect(const Size(360, 500), 'right', null, 0),
        const Rect.fromLTWH(0, 0, 360, 500),
      );
    },
  );
  test(
    'window placement, focus and minimize survive restoring a project',
    () async {
      final memory = MemoryWindows();
      final store = WorkspaceStore(persistence: memory);
      await store.load();
      for (final id in ['a', 'b']) {
        store.addArtifact(
          WorkspaceArtifact(id: id, title: id, kind: 'document'),
        );
      }
      store.openArtifact('a');
      expect(store.currentProject.presentation.modeOf('a'), 'maximized');
      store.restoreWindow('a');
      store.setWindowBounds('a', [40, 50, 500, 400]);
      store.maximizeWindow('a');
      store.restoreWindow('a');
      expect(store.currentProject.presentation.windowBounds['a'], [
        40,
        50,
        500,
        400,
      ]);
      store.currentProject.presentation.openArtifactIds.add('b');
      store.focusWindow('a');
      expect(store.currentProject.presentation.openArtifactIds.last, 'a');
      store.openBeside('b');
      expect(store.currentProject.presentation.modeOf('a'), 'left');
      expect(store.currentProject.presentation.modeOf('b'), 'right');
      store.minimizeArtifact('a');
      await store.flush();
      final restored = WorkspaceStore(persistence: memory);
      await restored.load();
      restored.openArtifact('a');
      expect(restored.currentProject.presentation.modeOf('a'), 'left');
      expect(restored.currentProject.presentation.openArtifactIds.last, 'a');
      expect(restored.currentConversation.attachedArtifactIds, isEmpty);
      restored.closeArtifact('a');
      expect(restored.currentProject.artifacts.length, 2);
      expect(restored.currentProject.presentation.activeArtifactId, 'b');
      await restored.flush();
    },
  );
}
