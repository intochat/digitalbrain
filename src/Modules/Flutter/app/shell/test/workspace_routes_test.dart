import 'package:digitalbrain_flutter_shell/workspace/workspace_app.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_routes.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_store.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/memory_workspace.dart';

void main() {
  testWidgets(
    'incoming project and settings routes select durable project and screen',
    (tester) async {
      tester.view.physicalSize = const Size(1440, 960);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);
      final store = WorkspaceStore(persistence: MemoryWorkspacePersistence());
      await store.load();
      final first = store.currentProject.id;
      final second = store.createProject('Second');
      await tester.pumpWidget(WorkspaceApp(store: store));
      await tester.pumpAndSettle();
      final app = tester.widget<MaterialApp>(find.byType(MaterialApp));
      final bridge = app.navigatorObservers!
          .whereType<WorkspaceRouteBridge>()
          .single;
      await bridge.didPushRouteInformation(
        RouteInformation(uri: Uri.parse('/projects/$first/workspace')),
      );
      await tester.pumpAndSettle();
      expect(store.currentProject.id, first);
      expect(find.byTooltip('Attach project work'), findsOneWidget);
      await bridge.didPushRouteInformation(
        RouteInformation(uri: Uri.parse('/projects/${second.id}')),
      );
      await tester.pumpAndSettle();
      expect(store.currentProject.id, second.id);
      expect(find.text('New conversation'), findsWidgets);
      await bridge.didPushRouteInformation(
        RouteInformation(uri: Uri.parse('/settings/appearance')),
      );
      await tester.pumpAndSettle();
      expect(find.text('Compact spacing'), findsOneWidget);
      expect(tester.takeException(), isNull);
    },
  );
}
