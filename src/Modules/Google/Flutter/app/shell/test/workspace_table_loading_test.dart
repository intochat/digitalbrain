import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_app.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_store.dart';
import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;

import 'workspace_data_window_test.dart' show WorkspaceClient;
import 'workspace_remote_controller_test.dart' show MemoryPersistence;

class DelayedTableClient extends WorkspaceClient {
  final tableReady = Completer<void>();

  @override
  Future<http.StreamedResponse> send(http.BaseRequest request) async {
    if (request.url.path.contains('/tables/')) await tableReady.future;
    return super.send(request);
  }
}

void main() {
  testWidgets('loaded workspace table replaces the pinned loading indicator', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1600, 1000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final store = WorkspaceStore(persistence: MemoryPersistence());
    final api = DelayedTableClient()
      ..revision = 1
      ..open = true;
    await tester.pumpWidget(
      WorkspaceApp(
        store: store,
        initialLocation: Uri.parse('/projects/${store.currentProject.id}'),
        programmingClient: DigitalBrainUiClient(
          baseUri: Uri.parse('http://test'),
          httpClient: api,
        ),
      ),
    );
    for (var i = 0; i < 5; i++) {
      await tester.pump(const Duration(milliseconds: 100));
    }
    expect(find.byType(CircularProgressIndicator), findsOneWidget);
    api.tableReady.complete();
    for (var i = 0; i < 5; i++) {
      await tester.pump(const Duration(milliseconds: 100));
    }
    expect(find.byType(UiDataTable), findsOneWidget);
    expect(find.text('From database'), findsOneWidget);
    expect(find.byType(CircularProgressIndicator), findsNothing);
    await tester.pumpWidget(const SizedBox());
    await api.events.close();
    store.dispose();
  });
}
