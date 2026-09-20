import 'dart:async';
import 'dart:convert';

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
  final events = StreamController<List<int>>.broadcast();
  int revision = 0;
  bool open = false;
  @override
  Future<http.StreamedResponse> send(http.BaseRequest request) async {
    if (request.url.path.endsWith('/events')) {
      return http.StreamedResponse(events.stream, 200);
    }
    final value = request.url.path.contains('/tables/')
        ? tableJson(1, 'From database')
        : {
            'revision': revision,
            'windows': revision == 0
                ? []
                : [
                    {
                      'id': 'window',
                      'title': 'Leads',
                      'view': {'id': 'table'},
                      'isOpen': open,
                    },
                  ],
          };
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
        'data: ${jsonEncode({'workspaceId': workspace, 'revision': revision})}\n\n',
      ),
    );
  }
}

void main() {
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
      snapshot: TableSnapshot.fromJson(tableJson(1, 'old')),
      read: (_, {offset = 0, limit = 25}) => ++calls == 1
          ? slow.future
          : calls == 2
          ? fast.future
          : Future.value(TableSnapshot.fromJson(tableJson(3, 'authoritative'))),
      update: (_, update) async =>
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
}
