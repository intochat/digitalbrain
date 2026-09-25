import 'dart:async';
import 'dart:convert';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_app.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_islands.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_store.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;

import 'workspace_remote_controller_test.dart' show MemoryPersistence;

class DockWorkspaceClient extends http.BaseClient {
  DockWorkspaceClient({this.holdClose = false, this.failClose = false});
  final bool holdClose;
  final bool failClose;
  final events = StreamController<List<int>>.broadcast();
  final closeGate = Completer<void>();
  int revision = 1;
  bool open = true;

  Map<String, dynamic> get snapshot => {
    'revision': revision,
    'windows': [
      {
        'id': 'remote',
        'title': 'Remote window',
        'view': {'id': 'table'},
        'isOpen': open,
      },
    ],
  };

  http.StreamedResponse _json(Map<String, dynamic> value) =>
      http.StreamedResponse(
        Stream.value(utf8.encode(jsonEncode(value))),
        200,
        headers: {'content-type': 'application/json'},
      );

  @override
  Future<http.StreamedResponse> send(http.BaseRequest request) async {
    final path = request.url.path;
    if (path.endsWith('/events')) {
      return http.StreamedResponse(events.stream, 200);
    }
    if (path.endsWith('/close')) {
      if (holdClose) await closeGate.future;
      if (failClose) {
        return http.StreamedResponse(Stream.value(utf8.encode('failed')), 500);
      }
      open = false;
      revision++;
      return _json(snapshot);
    }
    if (path.endsWith('/reopen')) {
      open = true;
      revision++;
      return _json(snapshot);
    }
    if (path.contains('/tables/')) {
      return _json({
        'id': 'table',
        'title': 'Leads',
        'revision': 1,
        'columns': [
          {'id': 'company', 'label': 'company', 'type': 'text'},
        ],
        'rows': [],
        'filters': [],
        'sort': null,
        'visibleColumns': ['company'],
        'totalRows': 0,
        'filteredRows': 0,
        'offset': 0,
        'limit': 25,
      });
    }
    return _json(snapshot);
  }
}

Future<void> pumpWorkspace(
  WidgetTester tester,
  WorkspaceStore store, {
  DigitalBrainUiClient? client,
}) async {
  tester.view.physicalSize = const Size(1400, 900);
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.resetPhysicalSize);
  addTearDown(tester.view.resetDevicePixelRatio);
  await tester.pumpWidget(
    WorkspaceApp(
      store: store,
      programmingClient: client,
      initialLocation: Uri.parse('/projects/${store.currentProject.id}'),
    ),
  );
  await tester.pumpAndSettle();
}

Finder dockIcon(String title) => find.descendant(
  of: find.byType(WorkspaceIslands),
  matching: find.byTooltip(title),
);

void main() {
  testWidgets('closing a local window removes it from the dock', (tester) async {
    final store = WorkspaceStore(persistence: MemoryPersistence());
    store.addArtifact(
      WorkspaceArtifact(id: 'note', title: 'Notes', kind: 'document'),
    );
    store.openArtifact('note', placement: 'floating');
    await pumpWorkspace(tester, store);
    expect(dockIcon('Notes'), findsWidgets);
    await tester.tap(find.byTooltip('Close editor'));
    await tester.pumpAndSettle();
    expect(store.currentProject.presentation.openArtifactIds, isEmpty);
    expect(dockIcon('Notes'), findsNothing);
    await tester.pumpWidget(const SizedBox());
    store.dispose();
  });

  testWidgets(
    'closing a remote window clears the dock before the server responds',
    (tester) async {
      final store = WorkspaceStore(persistence: MemoryPersistence());
      final api = DockWorkspaceClient(holdClose: true);
      final client = DigitalBrainUiClient(
        baseUri: Uri.parse('http://test'),
        httpClient: api,
      );
      await pumpWorkspace(tester, store, client: client);
      expect(dockIcon('Remote window'), findsWidgets);
      await tester.tap(find.byTooltip('Close editor'));
      await tester.pump();
      expect(store.currentProject.presentation.openArtifactIds, isEmpty);
      expect(dockIcon('Remote window'), findsNothing);
      api.closeGate.complete();
      await tester.pumpAndSettle();
      expect(store.currentProject.presentation.openArtifactIds, isEmpty);
      await tester.pumpWidget(const SizedBox());
      await api.events.close();
      store.dispose();
    },
  );

  testWidgets('a failed remote close keeps the window out of the dock', (
    tester,
  ) async {
    final store = WorkspaceStore(persistence: MemoryPersistence());
    final api = DockWorkspaceClient(failClose: true);
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://test'),
      httpClient: api,
    );
    await pumpWorkspace(tester, store, client: client);
    expect(dockIcon('Remote window'), findsWidgets);
    await tester.tap(find.byTooltip('Close editor'));
    await tester.pumpAndSettle();
    expect(store.currentProject.presentation.openArtifactIds, isEmpty);
    expect(dockIcon('Remote window'), findsNothing);
    await tester.pumpWidget(const SizedBox());
    await api.events.close();
    store.dispose();
  });

  test('a stale remote snapshot cannot reopen a window the user just closed', () {
    final store = WorkspaceStore(persistence: MemoryPersistence());
    final project = store.currentProject;
    WorkspaceSnapshot snapshot(int revision, bool open) => WorkspaceSnapshot(
      revision: revision,
      windows: [
        WorkspaceWindow(
          id: 'result',
          title: 'Leads',
          tableId: 'table',
          isOpen: open,
        ),
      ],
    );
    store.reconcileWorkspace(project, snapshot(1, true));
    expect(project.presentation.openArtifactIds, ['result']);
    store.closeArtifact('result');
    expect(project.presentation.openArtifactIds, isEmpty);
    store.reconcileWorkspace(project, snapshot(1, true));
    expect(project.presentation.openArtifactIds, isEmpty);
    store.reconcileWorkspace(project, snapshot(2, true));
    expect(project.presentation.openArtifactIds, ['result']);
    store.dispose();
  });
}
