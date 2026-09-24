import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_app.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_store.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

import 'workspace_remote_controller_test.dart' show MemoryPersistence;

void main() {
  testWidgets(
    'authenticated workspace waits for server membership and uses server id',
    (tester) async {
      tester.view.physicalSize = const Size(1440, 900);
      tester.view.devicePixelRatio = 1;
      final response = Completer<http.Response>();
      var requested = false;
      final client = DigitalBrainUiClient(
        baseUri: Uri.parse('http://kernel'),
        httpClient: MockClient((request) async {
          if (request.url.path == '/identity/workspaces') {
            requested = true;
            expect(request.method, 'POST');
            return response.future;
          }
          if (request.url.path.endsWith('/apps')) {
            return http.Response('[]', 200);
          }
          if (request.url.path.endsWith('/events')) {
            return http.Response('', 200);
          }
          return http.Response('{"revision":0,"windows":[]}', 200);
        }),
      )..accountId = 'alice-account';
      final store = WorkspaceStore(persistence: MemoryPersistence());
      await tester.pumpWidget(
        WorkspaceApp(store: store, programmingClient: client),
      );
      await tester.pumpAndSettle();
      await tester.tap(find.byTooltip('Workspaces'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('New workspace'));
      await tester.pumpAndSettle();
      await tester.enterText(
        find.widgetWithText(TextField, 'Workspace name'),
        'Second workspace',
      );
      await tester.tap(find.text('Create workspace'));
      await tester.pumpAndSettle();
      expect(requested, isTrue);
      expect(store.projects, hasLength(1));
      response.complete(
        http.Response('{"workspaceId":"server-owned-workspace"}', 200),
      );
      await tester.pumpAndSettle();
      expect(store.currentProject.id, 'server-owned-workspace');
      expect(store.currentProject.title, 'Second workspace');
      await tester.pumpWidget(const SizedBox.shrink());
      client.close();
      store.dispose();
      tester.view.resetPhysicalSize();
      tester.view.resetDevicePixelRatio();
    },
  );
}
