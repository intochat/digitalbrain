import 'dart:convert';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:test/test.dart';

void main() {
  final snapshot = <String, Object?>{
    'kind': 'table',
    'id': 'table:one',
    'title': 'People',
    'revision': 2,
    'columns': [
      {'id': 'age', 'label': 'Age', 'type': 'number'},
    ],
    'rows': [
      {
        'id': 'r1',
        'cells': [12],
      },
    ],
    'filters': [],
    'sort': null,
    'visibleColumns': ['age'],
    'totalRows': 1,
    'filteredRows': 1,
    'offset': 0,
    'limit': 50,
  };
  test(
    'table client reads pages and sends complete revision checked views',
    () async {
      final requests = <http.Request>[];
      final client = DigitalBrainUiClient(
        baseUri: Uri.parse('https://example.com'),
        httpClient: MockClient((request) async {
          requests.add(request);
          return http.Response(
            jsonEncode(
              request.url.path == '/kit/tables'
                  ? [
                      {'id': 'table:one', 'title': 'People', 'revision': 2},
                    ]
                  : snapshot,
            ),
            200,
          );
        }),
      );
      expect((await client.listTables()).single.id, 'table:one');
      final table = await client.readTable('table:one', offset: 50, limit: 25);
      expect(table.rows.single.cells.single, 12);
      expect(requests.last.url.queryParameters, {
        'offset': '50',
        'limit': '25',
      });
      await client.updateTableView(
        table.id,
        TableViewUpdate(
          expectedRevision: 2,
          filters: [TableFilter(columnId: 'age', operator: 'gte', value: 10)],
          sort: TableSort(columnId: 'age', descending: true),
          visibleColumns: ['age'],
        ),
      );
      expect(requests.last.method, 'PUT');
      expect(jsonDecode(requests.last.body), {
        'expectedRevision': 2,
        'filters': [
          {'columnId': 'age', 'operator': 'gte', 'value': 10},
        ],
        'sort': {'columnId': 'age', 'descending': true},
        'visibleColumns': ['age'],
      });
    },
  );
  test('conflict exposes status for reload without resubmitting', () async {
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('https://example.com'),
      httpClient: MockClient((_) async => http.Response('stale', 409)),
    );
    await expectLater(
      client.readTable('one'),
      throwsA(
        isA<TableRequestException>().having((e) => e.statusCode, 'status', 409),
      ),
    );
  });
  test('validation errors show server message without JSON wrapper', () async {
    for (final body in [
      {'error': 'Unsupported numeric precision.'},
      {'detail': 'The view is invalid.'},
    ]) {
      final client = DigitalBrainUiClient(
        baseUri: Uri.parse('https://example.com'),
        httpClient: MockClient(
          (_) async => http.Response(jsonEncode(body), 400),
        ),
      );
      await expectLater(
        client.readTable('one'),
        throwsA(
          isA<TableRequestException>().having(
            (e) => e.message,
            'message',
            body.values.single,
          ),
        ),
      );
    }
  });
}
