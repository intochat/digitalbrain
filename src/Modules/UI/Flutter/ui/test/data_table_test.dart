import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

TableSnapshot sample({
  int revision = 1,
  List<TableFilter> filters = const [],
}) => TableSnapshot(
  id: 't1',
  title: 'People',
  revision: revision,
  columns: const [
    TableColumn(id: 'name', label: 'Name', type: 'text'),
    TableColumn(id: 'age', label: 'Age', type: 'number'),
  ],
  rows: [
    TableRowData(id: 'r1', cells: ['Alice', 42]),
  ],
  filters: filters,
  visibleColumns: ['name', 'age'],
  totalRows: 80,
  filteredRows: 80,
  offset: 0,
  limit: 50,
);

void main() {
  testWidgets('table fills editor width and uses compact rows', (tester) async {
    tester.view.physicalSize = const Size(1000, 800);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    final controller = UiTableController(snapshot: sample());
    addTearDown(controller.dispose);
    await tester.pumpWidget(
      MaterialApp(
        theme: ThemeData(visualDensity: VisualDensity.compact),
        home: Scaffold(
          body: SingleChildScrollView(
            child: UiDataTable(controller: controller),
          ),
        ),
      ),
    );
    expect(tester.getSize(find.byType(DataTable)).width, greaterThan(900));
    final table = tester.widget<DataTable>(find.byType(DataTable));
    expect(table.dataRowMaxHeight, 36);
    expect(find.text('Clear filters'), findsNothing);
    expect(find.text('Clear sort'), findsNothing);
    await tester.tap(find.text('Alice'));
    await tester.pumpAndSettle();
    expect(find.byType(SelectableText), findsOneWidget);
    await tester.tap(find.text('Done'));
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
  });

  testWidgets('long column names fit a narrow screen filter dialog', (
    tester,
  ) async {
    tester.view.reset();
    tester.view.physicalSize = const Size(360, 800);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    final json = sample().toJson();
    json['columns'] = [
      {
        'id': 'name',
        'label': 'An extremely long descriptive column name for mobile layouts',
        'type': 'text',
      },
      {'id': 'age', 'label': 'Age', 'type': 'number'},
    ];
    final controller = UiTableController(
      snapshot: TableSnapshot.fromJson(json),
      update: (_, _) async => sample(),
    );
    addTearDown(controller.dispose);
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: SingleChildScrollView(
            child: UiDataTable(controller: controller),
          ),
        ),
      ),
    );
    await tester.tap(find.text('Filter'));
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
    await tester.tap(find.byKey(const Key('table_filter_column')));
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
  });

  testWidgets(
    'paging reads backend offsets and column visibility updates view',
    (tester) async {
      final offsets = <int>[];
      TableViewUpdate? changed;
      final controller = UiTableController(
        snapshot: sample(),
        read: (id, {offset = 0, limit = 50}) async {
          offsets.add(offset);
          return TableSnapshot.fromJson({
            ...sample().toJson(),
            'offset': offset,
          });
        },
        update: (id, update) async {
          changed = update;
          return TableSnapshot.fromJson({
            ...sample().toJson(),
            'revision': 2,
            'visibleColumns': update.visibleColumns,
          });
        },
      );
      addTearDown(controller.dispose);
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(body: UiDataTable(controller: controller)),
        ),
      );
      await tester.tap(find.byTooltip('Next page'));
      await tester.pumpAndSettle();
      expect(offsets, [50]);
      await tester.tap(find.byTooltip('Previous page'));
      await tester.pumpAndSettle();
      expect(offsets, [50, 0]);
      await tester.tap(find.text('Columns'));
      await tester.pumpAndSettle();
      await tester.tap(find.widgetWithText(CheckboxListTile, 'Age'));
      await tester.tap(find.text('Apply'));
      await tester.pumpAndSettle();
      expect(changed!.visibleColumns, ['name']);
      expect(find.text('Age'), findsNothing);
    },
  );

  for (final type in ['text', 'number', 'date', 'boolean']) {
    testWidgets(
      '$type filter sends typed values and null operator needs no input',
      (tester) async {
        final updates = <TableViewUpdate>[];
        final json = sample().toJson();
        json['columns'] = [
          {'id': 'name', 'label': 'Value', 'type': type},
        ];
        json['rows'] = [];
        json['visibleColumns'] = ['name'];
        final controller = UiTableController(
          snapshot: TableSnapshot.fromJson(json),
          update: (_, update) async {
            updates.add(update);
            return TableSnapshot.fromJson({
              ...json,
              'revision': updates.length + 1,
              'filters': update.filters.map((f) => f.toJson()).toList(),
            });
          },
        );
        addTearDown(controller.dispose);
        await tester.pumpWidget(
          MaterialApp(
            home: Scaffold(body: UiDataTable(controller: controller)),
          ),
        );
        await tester.tap(find.text('Filter'));
        await tester.pumpAndSettle();
        if (type != 'boolean') {
          if (type == 'number' || type == 'date') {
            await tester.enterText(
              find.byKey(const Key('table_filter_value')),
              type == 'number' ? 'NaN' : '2026-02-30',
            );
            await tester.tap(find.text('Apply'));
            await tester.pumpAndSettle();
            expect(updates, isEmpty);
            if (type == 'number') {
              for (final value in [
                '0.1234567890123456789',
                '9007199254740993',
                '1e-29',
                '1e-999',
              ]) {
                await tester.enterText(
                  find.byKey(const Key('table_filter_value')),
                  value,
                );
                await tester.tap(find.text('Apply'));
                await tester.pumpAndSettle();
                expect(
                  updates,
                  isEmpty,
                  reason: '$value must not be silently rounded',
                );
              }
            }
          }
          await tester.enterText(
            find.byKey(const Key('table_filter_value')),
            switch (type) {
              'number' => '-2.5',
              'date' => '2026-09-11',
              _ => 'Alice',
            },
          );
        }
        await tester.tap(find.text('Apply'));
        await tester.pumpAndSettle();
        expect(updates.last.filters.single.value, switch (type) {
          'number' => -2.5,
          'date' => '2026-09-11',
          'boolean' => true,
          _ => 'Alice',
        });
        await tester.tap(find.text('Filter'));
        await tester.pumpAndSettle();
        await tester.tap(find.byKey(const ValueKey('operator_name')));
        await tester.pumpAndSettle();
        await tester.tap(find.text('is empty').last);
        await tester.pumpAndSettle();
        expect(find.byKey(const Key('table_filter_value')), findsNothing);
        await tester.tap(find.text('Apply'));
        await tester.pumpAndSettle();
        expect(updates.last.filters.last.operator, 'isNull');
        expect(updates.last.filters.last.value, isNull);
      },
    );
  }
  testWidgets('sort, numeric filter and reset send complete server views', (
    tester,
  ) async {
    final updates = <TableViewUpdate>[];
    final controller = UiTableController(
      snapshot: sample(),
      read: (id, {offset = 0, limit = 50}) async => sample(),
      update: (id, update) async {
        updates.add(update);
        return sample(revision: updates.length + 1, filters: update.filters);
      },
    );
    addTearDown(controller.dispose);
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(body: UiDataTable(controller: controller)),
      ),
    );
    await tester.tap(find.text('Age'));
    await tester.pumpAndSettle();
    expect(updates.last.sort?.columnId, 'age');
    expect(updates.last.expectedRevision, 1);
    await tester.tap(find.text('Filter'));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('table_filter_column')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Age').last);
    await tester.pumpAndSettle();
    await tester.enterText(find.byKey(const Key('table_filter_value')), '20');
    await tester.tap(find.text('Apply'));
    await tester.pumpAndSettle();
    expect(updates.last.filters.single.value, 20);
    await tester.tap(find.byTooltip('Remove filter'));
    await tester.pumpAndSettle();
    expect(updates.last.filters, isEmpty);
    expect(find.text('1–1 of 80'), findsOneWidget);
  });

  testWidgets(
    'conflict reloads and explains latest state without retrying update',
    (tester) async {
      var writes = 0;
      var reads = 0;
      final controller = UiTableController(
        snapshot: sample(),
        read: (id, {offset = 0, limit = 50}) async {
          reads++;
          return sample(revision: 7);
        },
        update: (id, update) async {
          writes++;
          throw const TableRequestException(409, 'stale');
        },
      );
      addTearDown(controller.dispose);
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(body: UiDataTable(controller: controller)),
        ),
      );
      await tester.tap(find.text('Age'));
      await tester.pumpAndSettle();
      expect(writes, 1);
      expect(reads, 1);
      expect(controller.snapshot.revision, 7);
      expect(find.textContaining('changed elsewhere'), findsOneWidget);
    },
  );

  test('stale tool snapshots cannot overwrite newer shared state', () {
    final controller = UiTableController(snapshot: sample(revision: 7));
    controller.accept(sample(revision: 3));
    expect(controller.snapshot.revision, 7);
    controller.dispose();
  });
}
