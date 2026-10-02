import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/workspace/apps/apps_screen.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_app.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_store.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;

import 'first_run_test.dart' show FirstRunClient;
import 'workspace_remote_controller_test.dart' show MemoryPersistence;

class AppsRefreshClient extends FirstRunClient {
  int appReads = 0;
  @override
  Future<http.StreamedResponse> send(http.BaseRequest request) {
    if (request.url.path.endsWith('/apps')) appReads++;
    return super.send(request);
  }
}

void main() {
  for (final width in [1440.0, 390.0]) {
    testWidgets('account and app actions live in their menus at $width', (
      tester,
    ) async {
      tester.view.physicalSize = Size(width, 900);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      final store = WorkspaceStore(persistence: MemoryPersistence());
      final api = AppsRefreshClient();
      final client = DigitalBrainUiClient(
        baseUri: Uri.parse('http://test'),
        httpClient: api,
      );
      var switches = 0;
      await tester.pumpWidget(
        WorkspaceApp(
          store: store,
          programmingClient: client,
          onSwitchAccount: () async {
            switches++;
          },
        ),
      );
      await tester.pumpAndSettle();
      expect(find.text('Switch account'), findsNothing);
      expect(find.byTooltip('Apps'), findsNothing);
      expect(find.text('Send'), findsOneWidget);

      await tester.tap(find.byTooltip('Profile'));
      await tester.pumpAndSettle();
      expect(find.text('Switch account'), findsOneWidget);
      await tester.tap(find.text('Switch account'));
      await tester.pumpAndSettle();
      expect(switches, 1);
      expect(find.text('Switch account'), findsNothing);

      await tester.tap(find.byTooltip('Applications'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Apps'));
      await tester.pumpAndSettle();
      final packages = tester.widget<AppsScreen>(find.byType(AppsScreen));
      expect(packages.workspaceId, store.currentProject.id);
      packages.onClose();
      await tester.pumpAndSettle();
      expect(find.byType(AppsScreen), findsNothing);
      expect(store.currentProject.presentation.openArtifactIds, isEmpty);
      expect(tester.takeException(), isNull);
      for (final exit in ['back', 'open']) {
        final reads = api.appReads;
        await tester.tap(find.byTooltip('Applications'));
        await tester.pumpAndSettle();
        await tester.tap(find.text('Apps'));
        await tester.pumpAndSettle();
        final apps = tester.widget<AppsScreen>(find.byType(AppsScreen));
        if (exit == 'open') {
          await apps.onOpen!('assistant');
        } else {
          Navigator.of(tester.element(find.byType(AppsScreen))).pop();
        }
        await tester.pumpAndSettle();
        expect(
          api.appReads,
          greaterThan(reads),
          reason: '$exit refreshes the launcher',
        );
        expect(find.byType(AppsScreen), findsNothing);
        expect(tester.takeException(), isNull);
      }
      await tester.pumpWidget(const SizedBox());
      await api.events.close();
      store.dispose();
      client.close();
    });
  }
}
