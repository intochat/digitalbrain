import 'package:digitalbrain_flutter_shell/workspace/workspace_app.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_store.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/memory_workspace.dart';

void main() {
  testWidgets('brand returns home from a project and from empty Explore', (
    tester,
  ) async {
    final store = WorkspaceStore(
      persistence: MemoryWorkspacePersistence(),
      seedProject: false,
    );
    await tester.pumpWidget(WorkspaceApp(store: store));
    await tester.pumpAndSettle();
    await tester.tap(find.byTooltip('Go to homepage'));
    await tester.pumpAndSettle();
    expect(find.text('Room for your next idea'), findsOneWidget);
    store.createProject('Design studio');
    await tester.pumpAndSettle();
    await tester.tap(find.text('Design studio'));
    await tester.pumpAndSettle();
    expect(find.byTooltip('Switch conversation'), findsOneWidget);
    expect(find.byTooltip('Attach project work'), findsOneWidget);
    await tester.tap(find.byTooltip('Go to homepage'));
    await tester.pumpAndSettle();
    expect(find.text('Your projects'), findsOneWidget);
    expect(find.text('Design studio'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets(
    'empty homepage offers templates and creates an explicit specialist draft',
    (tester) async {
      final persistence = MemoryWorkspacePersistence();
      final store = WorkspaceStore(
        persistence: persistence,
        seedProject: false,
      );
      await tester.pumpWidget(WorkspaceApp(store: store));
      await tester.pumpAndSettle();
      expect(store.projects, isEmpty);
      expect(find.text('Your projects'), findsOneWidget);
      expect(find.text('New IntoChat project'), findsOneWidget);
      expect(
        find.descendant(
          of: find.byType(AppBar),
          matching: find.text('Explore'),
        ),
        findsNothing,
      );
      expect(
        find.descendant(
          of: find.byType(AppBar),
          matching: find.text('Projects'),
        ),
        findsNothing,
      );
      await tester.ensureVisible(find.text('Lead Researcher').first);
      await tester.tap(find.text('Lead Researcher').first);
      await tester.pumpAndSettle();
      await tester.enterText(
        find.byKey(const Key('project-intent')),
        'Find European manufacturers',
      );
      await tester.tap(find.text('Start project'));
      await tester.pumpAndSettle();
      expect(store.currentConversation.selectedAgentId, 'leads');
      expect(store.currentConversation.draft, 'Find European manufacturers');
      expect(store.currentConversation.messages, isEmpty);
      expect(tester.takeException(), isNull);
      await tester.enterText(find.byType(TextField), 'Updated research goal');
      await tester.binding.setSurfaceSize(const Size(360, 780));
      addTearDown(() => tester.binding.setSurfaceSize(null));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 400));
      await store.flush();
      final restored = WorkspaceStore(
        persistence: persistence,
        seedProject: false,
      );
      await restored.load();
      expect(restored.currentConversation.draft, 'Updated research goal');
      expect(restored.currentConversation.selectedAgentId, 'leads');
    },
  );

  test(
    'empty saved workspace remains empty and legacy projects survive',
    () async {
      final persistence = MemoryWorkspacePersistence();
      final empty = WorkspaceStore(
        persistence: persistence,
        seedProject: false,
      );
      await empty.load();
      await empty.save();
      final restored = WorkspaceStore(
        persistence: persistence,
        seedProject: false,
      );
      await restored.load();
      expect(restored.projects, isEmpty);
      restored.createProject('Existing work');
      await restored.flush();
      final again = WorkspaceStore(
        persistence: persistence,
        seedProject: false,
      );
      await again.load();
      expect(again.currentProject.title, 'Existing work');
    },
  );
}
