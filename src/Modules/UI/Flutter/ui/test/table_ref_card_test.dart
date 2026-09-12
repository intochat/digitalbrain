import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_chat_core/flutter_chat_core.dart';
import 'package:flutter_test/flutter_test.dart';

TableSnapshot leads({int revision = 1, List<TableFilter> filters = const []}) =>
    TableSnapshot(
      id: 'chtable-leads',
      title: 'UK construction',
      revision: revision,
      columns: const [
        TableColumn(id: 'name', label: 'name', type: 'text'),
        TableColumn(
          id: 'employee_count',
          label: 'employee_count',
          type: 'number',
        ),
      ],
      rows: [
        TableRowData(id: 'row-0', cells: ['Thames Roofing Ltd', 64]),
        TableRowData(id: 'row-1', cells: ['Northgate Builders', 120]),
      ],
      filters: filters,
      visibleColumns: ['name', 'employee_count'],
      totalRows: 12,
      filteredRows: filters.isEmpty ? 12 : 5,
      offset: 0,
      limit: 50,
    );

CustomMessage tableCard(String id) => CustomMessage(
  id: id,
  authorId: 'assistant',
  createdAt: DateTime.utc(2026, 9, 12),
  metadata: const {
    'kind': 'table-ref',
    'name': 'chtable-leads',
    'caption': 'UK construction',
  },
);

void main() {
  test('table-ref parts round-trip through metadata', () {
    final part = UiPart.tryParse(tableCard('m1').metadata);
    expect(part, isA<UiTableRefPart>());
    final table = part! as UiTableRefPart;
    expect(table.name, 'chtable-leads');
    expect(table.caption, 'UK construction');
    expect(table.copyText, 'UK construction');
    expect(UiPart.tryParse(table.toMetadata()), isA<UiTableRefPart>());
  });

  testWidgets('a table-ref card reads the table and renders it live', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1000, 800);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    final reads = <String>[];
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: SingleChildScrollView(
            child: Builder(
              builder: (context) => UiChatBuilders.customMessageBuilder(
                context,
                tableCard('m1'),
                0,
                isSentByMe: false,
                onReadTable: (id, {offset = 0, limit = 50}) async {
                  reads.add(id);
                  return leads();
                },
                onUpdateTableView: (id, update) async =>
                    leads(revision: 2, filters: update.filters),
              ),
            ),
          ),
        ),
      ),
    );
    expect(find.byKey(const Key('ui_table_ref_loading')), findsOneWidget);
    await tester.pumpAndSettle();
    expect(reads, ['chtable-leads']);
    expect(find.byType(UiDataTable), findsOneWidget);
    expect(find.text('UK construction'), findsOneWidget);
    expect(find.text('Thames Roofing Ltd'), findsOneWidget);
    expect(find.text('1–2 of 12'), findsOneWidget);
  });

  testWidgets('a table-ref card without a reader shows its caption', (
    tester,
  ) async {
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: Builder(
            builder: (context) => UiChatBuilders.customMessageBuilder(
              context,
              tableCard('m1'),
              0,
              isSentByMe: false,
            ),
          ),
        ),
      ),
    );
    await tester.pump();
    expect(
      find.byKey(const Key('ui_table_ref_offline_chtable-leads')),
      findsOneWidget,
    );
    expect(find.byType(UiDataTable), findsNothing);
  });
}
