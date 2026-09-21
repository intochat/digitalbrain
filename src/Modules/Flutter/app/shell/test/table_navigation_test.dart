import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'workspace_data_window_test.dart' show tableJson;

void main() {
  testWidgets(
    'paging replaces visible rows and previous restores the first page',
    (tester) async {
      final controller = UiTableController(
        snapshot: TableSnapshot.fromJson(tableJson(1, 'First page company')),
        read: (_, {offset = 0, limit = 25}) async => TableSnapshot.fromJson({
          ...tableJson(
            1,
            offset == 0 ? 'First page company' : 'Next page company',
          ),
          'offset': offset,
          'limit': limit,
        }),
      );
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(body: UiDataTable(controller: controller)),
        ),
      );
      await tester.tap(find.byTooltip('Next page'));
      await tester.pumpAndSettle();
      expect(find.text('Next page company'), findsOneWidget);
      expect(find.text('First page company'), findsNothing);
      await tester.tap(find.byTooltip('Previous page'));
      await tester.pumpAndSettle();
      expect(find.text('First page company'), findsOneWidget);
      await tester.pumpWidget(const SizedBox());
      controller.dispose();
    },
  );

  testWidgets(
    'column gestures render ascending and descending server results',
    (tester) async {
      final controller = UiTableController(
        snapshot: TableSnapshot.fromJson(tableJson(1, 'Unsorted company')),
        update: (_, update) async => TableSnapshot.fromJson({
          ...tableJson(
            update.expectedRevision + 1,
            update.sort?.descending == true ? 'Zulu first' : 'Alpha first',
          ),
          'sort': update.sort?.toJson(),
        }),
      );
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(body: UiDataTable(controller: controller)),
        ),
      );
      await tester.tap(find.text('company').first);
      await tester.pumpAndSettle();
      expect(find.text('Alpha first'), findsOneWidget);
      await tester.tap(
        find.descendant(
          of: find.byType(DataTable),
          matching: find.text('company'),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.text('Zulu first'), findsOneWidget);
      expect(find.text('Alpha first'), findsNothing);
      await tester.pumpWidget(const SizedBox());
      controller.dispose();
    },
  );
}
