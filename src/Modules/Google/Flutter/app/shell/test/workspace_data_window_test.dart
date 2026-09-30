import 'dart:async';
import 'dart:convert';

import 'application_fixture.dart';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_app.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_store.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;

import 'workspace_remote_controller_test.dart' show MemoryPersistence;

Map<String, dynamic> tableJson(int revision, String company) => {
  'id': 'table',
  'title': 'Leads',
  'revision': revision,
  'columns': [
    {'id': 'company', 'label': 'company', 'type': 'text'},
  ],
  'rows': [
    {
      'id': 'row',
      'cells': [company],
    },
  ],
  'filters': [],
  'sort': null,
  'visibleColumns': ['company'],
  'totalRows': 60,
  'filteredRows': 60,
  'offset': 0,
  'limit': 25,
};

class WorkspaceClient extends http.BaseClient {
  List<Map<String, dynamic>> history = [];
  final events = StreamController<List<int>>.broadcast();
  int revision = 0;
  bool open = false;
  @override
  Future<http.StreamedResponse> send(http.BaseRequest request) async {
    if (request.url.path.endsWith('/events')) {
      return http.StreamedResponse(events.stream, 200);
    }
    final application = applicationResponse(request);
    if (application != null &&
        request.url.queryParameters['kind'] == 'layout' &&
        (request.url.queryParameters['name'] ?? '').endsWith('/messages')) {
      application['children'] = [
        for (final turn in history) ...[
          {'kind': 'card', 'name': "${turn['runId']}/user"},
          {'kind': 'card', 'name': "${turn['runId']}/assistant"},
        ],
      ];
    }
    if (request.url.queryParameters['kind'] == 'card') {
      final name = request.url.queryParameters['name']!;
      final turn = history.firstWhere(
        (turn) => name.startsWith('${turn['runId']}/'),
      );
      return http.StreamedResponse(
        Stream.value(
          utf8.encode(
            jsonEncode({
              'title': '',
              'body': name.endsWith('/user')
                  ? turn['userText']
                  : turn['assistantText'],
              'children': [],
            }),
          ),
        ),
        200,
      );
    }
    final value =
        application ??
        (request.url.path.contains('/conversations/')
            ? {'revision': 4, 'activeRunId': null, 'turns': history}
            : request.url.path.contains('/tables/')
            ? tableJson(1, 'From database')
            : {
                'revision': revision,
                'windows': revision == 0
                    ? []
                    : [
                        {
                          'id': 'window',
                          'title': 'Leads',
                          'reference': {'kind': 'table', 'neuronId': 'table'},
                          'isOpen': open,
                        },
                      ],
              });
    return http.StreamedResponse(
      Stream.value(utf8.encode(jsonEncode(value))),
      200,
      headers: {'content-type': 'application/json'},
    );
  }

  void publish(String workspace, {required bool isOpen}) {
    revision++;
    open = isOpen;
    events.add(
      utf8.encode(
        'data: ${jsonEncode({'brainId': workspace, 'revision': revision})}\n\n',
      ),
    );
  }
}

void main() {
  testWidgets('a single filter can be cleared through the server view', (
    tester,
  ) async {
    TableViewUpdate? submitted;
    final controller = UiTableController(
      workspace: 'w',
      snapshot: TableSnapshot.fromJson({
        ...tableJson(1, 'Filtered'),
        'filters': [
          {'columnId': 'company', 'operator': 'eq', 'value': 'Filtered'},
        ],
      }),
      update: (_, _, update) async {
        submitted = update;
        return TableSnapshot.fromJson(tableJson(2, 'All rows'));
      },
    );
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(body: UiDataTable(controller: controller)),
      ),
    );
    await tester.tap(find.text('Clear filters'));
    await tester.pumpAndSettle();
    expect(submitted!.filters, isEmpty);
    expect(submitted!.expectedRevision, 1);
    expect(find.text('All rows'), findsOneWidget);
    await tester.pumpWidget(const SizedBox());
    controller.dispose();
  });
  testWidgets(
    'reload opens the requested workspace after asynchronous storage loads',
    (tester) async {
      final store = WorkspaceStore(persistence: MemoryPersistence());
      await tester.pumpWidget(
        WorkspaceApp(
          store: store,
          initialLocation: Uri.parse('/projects/${store.currentProject.id}'),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.text('Connect to open applications.'), findsOneWidget);
      await tester.pumpWidget(const SizedBox());
      store.dispose();
    },
  );
  testWidgets(
    'history recovery retains identical requests from distinct runs',
    (tester) async {
      final store = WorkspaceStore(persistence: MemoryPersistence());
      store.currentConversation.threadId = 'thread';
      final api = WorkspaceClient()
        ..history = [
          {
            'runId': 'one',
            'userText': 'Repeated request',
            'assistantText': 'First answer',
            'resultIds': [],
          },
          {
            'runId': 'two',
            'userText': 'Repeated request',
            'assistantText': 'Second answer',
            'resultIds': [],
          },
        ];
      final client = DigitalBrainUiClient(
        baseUri: Uri.parse('http://test'),
        httpClient: api,
      );
      await tester.pumpWidget(
        WorkspaceApp(store: store, programmingClient: client),
      );
      await tester.pumpAndSettle();
      expect(find.text('Repeated request'), findsNWidgets(2));
      expect(find.text('First answer'), findsOneWidget);
      expect(find.text('Second answer'), findsOneWidget);
      await tester.pumpWidget(const SizedBox());
      await api.events.close();
    },
  );
  testWidgets(
    'workspace update opens existing table controls without duplicate windows',
    (tester) async {
      tester.view.physicalSize = const Size(1600, 1000);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      final store = WorkspaceStore(persistence: MemoryPersistence());
      final api = WorkspaceClient();
      final client = DigitalBrainUiClient(
        baseUri: Uri.parse('http://test'),
        httpClient: api,
      );
      await tester.pumpWidget(
        WorkspaceApp(store: store, programmingClient: client),
      );
      await tester.pumpAndSettle();
      await tester.tap(find.text('My project').first);
      await tester.pumpAndSettle();
      api.publish(store.currentProject.id, isOpen: true);
      await tester.pumpAndSettle();
      expect(find.byType(UiDataTable), findsOneWidget);
      expect(find.text('From database'), findsOneWidget);
      expect(find.text('Filter'), findsOneWidget);
      expect(find.byTooltip('Next page'), findsOneWidget);
      api.publish(store.currentProject.id, isOpen: true);
      await tester.pumpAndSettle();
      expect(find.byType(UiDataTable), findsOneWidget);
      expect(
        store.currentProject.artifacts.single.data.containsKey('rows'),
        isFalse,
      );
      api.publish(store.currentProject.id, isOpen: false);
      await tester.pumpAndSettle();
      expect(find.byType(UiDataTable), findsNothing);
      await tester.pumpWidget(const SizedBox());
      await api.events.close();
    },
  );

  test('late table read cannot overwrite newer page and conflict reloads authoritative revision', () async {
    final slow = Completer<TableSnapshot>();
    final fast = Completer<TableSnapshot>();
    var calls = 0;
    final controller = UiTableController(
      workspace: 'w',
      snapshot: TableSnapshot.fromJson(tableJson(1, 'old')),
      read: (_, _, {offset = 0, limit = 25}) => ++calls == 1
          ? slow.future
          : calls == 2
          ? fast.future
          : Future.value(TableSnapshot.fromJson(tableJson(3, 'authoritative'))),
      update: (_, _, update) async =>
          throw const TableRequestException(409, 'conflict'),
    );
    final first = controller.reload();
    final second = controller.reload();
    fast.complete(TableSnapshot.fromJson(tableJson(2, 'new')));
    await second;
    slow.complete(TableSnapshot.fromJson(tableJson(1, 'stale')));
    await first;
    expect(controller.snapshot.rows.single.cells.single, 'new');
    await controller.change(filters: []);
    expect(controller.snapshot.revision, 3);
    expect(controller.error, contains('changed elsewhere'));
    controller.dispose();
  });
  test('a refined saved view without rows re-reads the refined rows', () async {
    var reads = 0;
    final controller = UiTableController(
      workspace: 'w',
      snapshot: TableSnapshot.fromJson(tableJson(1, 'Berlin')),
      read: (_, _, {offset = 0, limit = 25}) async {
        reads++;
        return TableSnapshot.fromJson({
          ...tableJson(2, 'London'),
          'filteredRows': 6,
          'filters': [
            {'columnId': 'company', 'operator': 'eq', 'value': 'London'},
          ],
        });
      },
    );
    await controller.acceptSavedView(
      TableSnapshot.fromJson({
        ...tableJson(2, 'Leads'),
        'rows': <Object?>[],
        'filteredRows': 0,
        'filters': [
          {'columnId': 'company', 'operator': 'eq', 'value': 'London'},
        ],
      }),
    );
    expect(reads, 1);
    expect(controller.snapshot.rows.single.cells.single, 'London');
    expect(controller.snapshot.filters.single.value, 'London');
    controller.dispose();
  });
  test('an empty saved view re-reads once without looping', () async {
    var reads = 0;
    final controller = UiTableController(
      workspace: 'w',
      snapshot: TableSnapshot.fromJson(tableJson(1, 'old')),
      read: (_, _, {offset = 0, limit = 25}) async {
        reads++;
        return TableSnapshot.fromJson({
          ...tableJson(2, 'Leads'),
          'rows': <Object?>[],
          'filteredRows': 0,
        });
      },
    );
    await controller.acceptSavedView(
      TableSnapshot.fromJson({
        ...tableJson(2, 'Leads'),
        'rows': <Object?>[],
        'filteredRows': 0,
      }),
    );
    expect(reads, 1);
    expect(controller.snapshot.rows, isEmpty);
    controller.dispose();
  });
}
