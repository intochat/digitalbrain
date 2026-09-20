import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('workspace snapshot retains closed descriptors for explicit reopen', () {
    final state = WorkspaceSnapshot.fromJson({
      'revision': 4,
      'windows': [
        {
          'id': 'window',
          'title': 'Leads',
          'view': {'id': 'table'},
          'isOpen': false,
        },
      ],
    });
    expect(state.revision, 4);
    expect(state.windows.single.tableId, 'table');
    expect(state.windows.single.isOpen, false);
    expect(() => state.windows.clear(), throwsUnsupportedError);
  });

  test('query table contract preserves scalar cells and server counts', () {
    final table = TableSnapshot.fromJson({
      'id': 'table',
      'title': 'Leads',
      'revision': 2,
      'kind': 'table',
      'columns': [
        {'id': 'id', 'label': 'Id', 'type': 'number'},
        {'id': 'name', 'label': 'Name', 'type': 'text'},
        {'id': 'date', 'label': 'Date', 'type': 'date'},
      ],
      'rows': [
        {
          'id': '55',
          'cells': [55, null, '2026-09-20'],
        },
      ],
      'filters': [
        {'columnId': 'id', 'operator': 'gte', 'value': 55},
      ],
      'sort': {'columnId': 'id', 'descending': true},
      'visibleColumns': ['id', 'name', 'date'],
      'totalRows': 60,
      'filteredRows': 6,
      'offset': 0,
      'limit': 25,
    });
    expect(table.rows.single.cells, [55, null, '2026-09-20']);
    expect(table.totalRows, 60);
    expect(table.filteredRows, 6);
    expect(table.filters.single.value, 55);
    expect(table.sort!.descending, true);
  });
}
