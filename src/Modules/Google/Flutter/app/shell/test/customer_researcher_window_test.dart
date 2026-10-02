import 'dart:convert';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_app.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_store.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;

import 'first_run_test.dart' show FirstRunClient;
import 'workspace_remote_controller_test.dart' show MemoryPersistence;

class ResearchWindowClient extends FirstRunClient {
  ResearchWindowClient(this.store);
  final WorkspaceStore store;
  bool opened = false;
  String get surface =>
      '${store.currentProject.id}/applications/customer-researcher/window/surface';
  WorkspaceSnapshot get snapshot => WorkspaceSnapshot(
    revision: 1,
    windows: [
      WorkspaceWindow(
        id: 'research-window',
        title: 'Customer Researcher',
        kind: 'surface',
        neuronId: surface,
        isOpen: true,
      ),
    ],
  );
  http.StreamedResponse json(Object value) => http.StreamedResponse(
    Stream.value(utf8.encode(jsonEncode(value))),
    200,
    headers: {'content-type': 'application/json'},
  );
  @override
  Future<http.StreamedResponse> send(http.BaseRequest request) async {
    if (request.url.path.endsWith('/apps')) {
      return json([
        {
          'id': 'intochat/customer-researcher',
          'title': 'Customer Researcher',
          'description': 'Research companies',
          'app': {
            'operations': [
              {'name': 'open'},
            ],
          },
        },
      ]);
    }
    if (request.url.path ==
        Uri.parse(
          DigitalBrainUiClient.appOpenPath(
            store.currentProject.id,
            'intochat/customer-researcher',
          ),
        ).path) {
      // Server publishes workspace membership before its open response arrives.
      opened = true;
      store.reconcileWorkspace(store.currentProject, snapshot);
      return json({
        'id': 'research-window',
        'title': 'Customer Researcher',
        'surface': surface,
      });
    }
    if (request.url.path.endsWith('/apps/node') &&
        request.url.queryParameters['name'] == surface) {
      return json({
        'children': [
          {'kind': 'text', 'name': '$surface/status'},
        ],
      });
    }
    if (request.url.path.endsWith('/apps/node') &&
        request.url.queryParameters['name'] == '$surface/status') {
      return json({'markdown': 'Research window ready'});
    }
    if (opened &&
        (request.url.path.endsWith('/windows') ||
            request.url.path.contains('/windows/'))) {
      return json({
        'revision': 1,
        'windows': [
          {
            'id': 'research-window',
            'title': 'Customer Researcher',
            'reference': {'kind': 'surface', 'neuronId': surface},
            'isOpen': true,
          },
        ],
      });
    }
    return super.send(request);
  }
}

void main() {
  test('workspace snapshots repair surface metadata and do not reopen open windows', () {
    final store = WorkspaceStore(persistence: MemoryPersistence());
    final api = ResearchWindowClient(store);
    store.currentProject.artifacts.add(
      WorkspaceArtifact(
        id: 'research-window',
        title: 'Customer Researcher',
        kind: 'app',
        data: {'app': 'images'},
      ),
    );
    var remoteCommands = 0;
    store.onRemoteWindowAction = (_, _, _) {
      remoteCommands++;
    };
    store.reconcileWorkspace(store.currentProject, api.snapshot);
    final artifact = store.currentProject.artifacts.singleWhere(
      (a) => a.id == 'research-window',
    );
    expect(artifact.kind, 'surface');
    expect(artifact.remoteManaged, isTrue);
    expect(artifact.data['surface'], {'kind': 'surface', 'name': api.surface});
    store.openArtifact(artifact.id);
    store.openArtifact(artifact.id, placement: 'floating');
    expect(remoteCommands, 0);
    store.dispose();
  });

  testWidgets(
    'researcher opens when workspace update precedes application response',
    (tester) async {
      tester.view.physicalSize = const Size(1600, 1000);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      final store = WorkspaceStore(persistence: MemoryPersistence());
      final api = ResearchWindowClient(store);
      final client = DigitalBrainUiClient(
        baseUri: Uri.parse('http://test'),
        httpClient: api,
      );
      await tester.pumpWidget(
        WorkspaceApp(store: store, programmingClient: client),
      );
      await tester.pumpAndSettle();
      await tester.tap(find.byTooltip('Applications'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Customer Researcher'));
      await tester.pumpAndSettle();
      expect(tester.takeException(), isNull);
      expect(find.text('Research window ready'), findsOneWidget);
      expect(
        store.currentProject.artifacts
            .singleWhere((a) => a.id == 'research-window')
            .kind,
        'surface',
      );
      await tester.pumpWidget(const SizedBox());
      await api.events.close();
      client.close();
      store.dispose();
    },
  );
}
