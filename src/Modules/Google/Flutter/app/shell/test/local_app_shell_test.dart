import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_app.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_store.dart';

import 'workspace_remote_controller_test.dart' show MemoryPersistence;

void main() {
  test('app launch minimizes others and preserves a restored size', () {
    final store = WorkspaceStore(
      persistence: MemoryPersistence(),
      seedProject: false,
    )..createProject('Personal');
    store.launchLocalApp('files');
    expect(store.currentProject.presentation.modeOf('app-files'), 'maximized');
    store.restoreWindow('app-files');
    store.launchLocalApp('images');
    expect(
      store.currentProject.presentation.minimizedArtifactIds,
      contains('app-files'),
    );
    store.launchLocalApp('files');
    expect(store.currentProject.presentation.modeOf('app-files'), 'floating');
    expect(
      store.currentProject.artifacts.where((a) => a.id == 'app-files'),
      hasLength(1),
    );
  });
  test('activity opens once as a workspace application', () {
    final store = WorkspaceStore(
      persistence: MemoryPersistence(),
      seedProject: false,
    )..createProject('Personal');
    final first = store.launchLocalApp('activity');
    final again = store.launchLocalApp('activity');
    expect(first.id, 'app-activity');
    expect(first.title, 'Activity');
    expect(again.id, first.id);
    expect(
      store.currentProject.artifacts.where((a) => a.id == first.id),
      hasLength(1),
    );
  });
  testWidgets(
    'empty installation starts in a workspace with floating controls',
    (tester) async {
      tester.view.physicalSize = const Size(1440, 900);
      tester.view.devicePixelRatio = 1;
      final store = WorkspaceStore(
        persistence: MemoryPersistence(),
        seedProject: false,
      );
      await tester.pumpWidget(WorkspaceApp(store: store));
      await tester.pumpAndSettle();
      expect(find.text('Your projects'), findsNothing);
      expect(find.byTooltip('Applications'), findsOneWidget);
      expect(find.text('Compute —'), findsOneWidget);
      expect(store.currentProject.title, 'Personal');
      await tester.pumpWidget(const SizedBox.shrink());
      tester.view.resetPhysicalSize();
      tester.view.resetDevicePixelRatio();
    },
  );
}
