import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/chat/agent_chat_app.dart';
import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

TableSnapshot table({int revision = 1, String value = 'Alice'}) =>
    TableSnapshot(
      id: 't1',
      title: 'People',
      revision: revision,
      columns: const [TableColumn(id: 'name', label: 'Name', type: 'text')],
      rows: [
        TableRowData(id: 'r1', cells: [value]),
      ],
      filters: [],
      visibleColumns: ['name'],
      totalRows: 1,
      filteredRows: 1,
      offset: 0,
      limit: 50,
    );

void main() {
  testWidgets('failed table tool displays error without success claim', (
    tester,
  ) async {
    final events = StreamController<AgentEvent>();
    await tester.pumpWidget(
      AgentChatApp(
        onRun: ({
          required threadId,
          required runId,
          parentRunId,
          required text,
        }) => events.stream,
      ),
    );
    await tester.enterText(find.byType(TextField), 'Filter this');
    await tester.tap(find.byTooltip('Send message'));
    events.add(
      AgentEvent({
        'type': 'TOOL_CALL_START',
        'toolCallId': 'bad',
        'toolCallName': 'update_table_view',
      }),
    );
    events.add(
      AgentEvent({
        'type': 'TOOL_CALL_RESULT',
        'toolCallId': 'bad',
        'content': {
          'kind': 'tableError',
          'code': 'revision_conflict',
          'message': 'Read the latest table before trying again.',
        },
      }),
    );
    await tester.pump();
    expect(
      find.text('Read the latest table before trying again.'),
      findsOneWidget,
    );
    expect(find.textContaining('Complete'), findsNothing);
    await tester.pumpWidget(const SizedBox.shrink());
    unawaited(events.close());
  });
  testWidgets(
    'saved table opens after restart and attaches only ID context to next request',
    (tester) async {
      String? sent;
      var reads = 0;
      await tester.pumpWidget(
        AgentChatApp(
          onListTables: () async => [
            const TableSummary(id: 't1', title: 'People', revision: 1),
          ],
          onReadTable: (id, {offset = 0, limit = 50}) async {
            reads++;
            return table();
          },
          onRun:
              ({
                required threadId,
                required runId,
                parentRunId,
                required text,
              }) {
                sent = text;
                return Stream.fromIterable([
                  AgentEvent({'type': 'RUN_FINISHED'}),
                ]);
              },
        ),
      );
      await tester.tap(find.byTooltip('Saved tables'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('People'));
      await tester.pumpAndSettle();
      expect(find.byType(KitDataTable), findsOneWidget);
      expect(reads, 1);
      await tester.enterText(find.byType(TextField), 'How many?');
      await tester.tap(find.byTooltip('Send message'));
      await tester.pumpAndSettle();
      expect(sent, contains('Active table context'));
      expect(sent, contains('t1'));
      expect(sent, isNot(contains('Alice')));
      expect(find.text('How many?'), findsOneWidget);
      expect(find.textContaining('Active table context'), findsNothing);
      await tester.tap(find.byTooltip('New conversation'));
      await tester.pumpAndSettle();
      await tester.tap(find.byTooltip('Saved tables'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('People'));
      await tester.pumpAndSettle();
      expect(reads, 2);
    },
  );

  testWidgets(
    'tool results render table kit and share newer snapshots by stable ID',
    (tester) async {
      final events = StreamController<AgentEvent>();
      await tester.pumpWidget(
        AgentChatApp(
          onRun: ({
            required threadId,
            required runId,
            parentRunId,
            required text,
          }) => events.stream,
        ),
      );
      await tester.enterText(find.byType(TextField), 'Make a table');
      await tester.tap(find.byTooltip('Send message'));
      events.add(
        AgentEvent({
          'type': 'TOOL_CALL_RESULT',
          'toolCallId': 'a',
          'content': table().toJson(),
        }),
      );
      await tester.pump();
      expect(find.byType(KitDataTable), findsOneWidget);
      final first = tester
          .widget<KitDataTable>(find.byType(KitDataTable))
          .controller;
      events.add(
        AgentEvent({
          'type': 'TOOL_CALL_RESULT',
          'toolCallId': 'b',
          'content': table(revision: 2, value: 'Bob').toJson(),
        }),
      );
      await tester.pump();
      final tables = tester
          .widgetList<KitDataTable>(find.byType(KitDataTable))
          .toList();
      expect(tables.every((t) => identical(t.controller, first)), isTrue);
      expect(first.snapshot.rows.single.cells.single, 'Bob');
      await tester.pumpWidget(const SizedBox.shrink());
      unawaited(events.close());
    },
  );
}
