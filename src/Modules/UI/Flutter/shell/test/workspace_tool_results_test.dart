import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_app.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_store.dart';
import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/memory_workspace.dart';

TableSnapshot table({int revision = 1, String value = 'Alice'}) =>
    TableSnapshot(
      id: 'table-people',
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

Future<StreamController<AgentEvent>> sendFirstMessage(
  WidgetTester tester,
  WorkspaceStore store,
) async {
  final events = StreamController<AgentEvent>();
  await tester.pumpWidget(
    WorkspaceApp(
      store: store,
      onRun: ({
        required threadId,
        required runId,
        parentRunId,
        required text,
      }) => events.stream,
    ),
  );
  await tester.pumpAndSettle();
  await tester.tap(find.text('My project'));
  await tester.pumpAndSettle();
  await tester.tap(find.byTooltip('New conversation'));
  await tester.pumpAndSettle();
  await tester.enterText(find.byType(TextField), 'Make a table');
  await tester.pump();
  await tester.tap(find.byTooltip('Send message'));
  await tester.pump();
  return events;
}

Future<void> finish(
  WidgetTester tester,
  StreamController<AgentEvent> events,
) async {
  events.add(AgentEvent({'type': 'RUN_FINISHED'}));
  await events.close();
  await tester.pumpAndSettle();
  await tester.pump(const Duration(seconds: 1));
}

void main() {
  testWidgets(
    'a table tool result opens in the working area and a newer result reuses its controller',
    (tester) async {
      final store = WorkspaceStore(persistence: MemoryWorkspacePersistence());
      final events = await sendFirstMessage(tester, store);
      events.add(
        AgentEvent({
          'type': 'TOOL_CALL_START',
          'toolCallId': 'a',
          'toolCallName': 'create_table',
        }),
      );
      events.add(
        AgentEvent({
          'type': 'TOOL_CALL_RESULT',
          'toolCallId': 'a',
          'content': table().toJson(),
        }),
      );
      await tester.pumpAndSettle();
      expect(find.byType(UiDataTable), findsOneWidget);
      final first = tester
          .widget<UiDataTable>(find.byType(UiDataTable))
          .controller;
      events.add(
        AgentEvent({
          'type': 'TOOL_CALL_RESULT',
          'toolCallId': 'b',
          'content': table(revision: 2, value: 'Bob').toJson(),
        }),
      );
      await tester.pumpAndSettle();
      final tables = tester
          .widgetList<UiDataTable>(find.byType(UiDataTable))
          .toList();
      expect(tables.every((t) => identical(t.controller, first)), isTrue);
      expect(first.snapshot.rows.single.cells.single, 'Bob');
      expect(store.currentProject.artifacts.single.kind, 'table');
      await finish(tester, events);
    },
  );

  testWidgets(
    'a table error result stays a tool tile and creates no artifact',
    (tester) async {
      final store = WorkspaceStore(persistence: MemoryWorkspacePersistence());
      final events = await sendFirstMessage(tester, store);
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
      await tester.pumpAndSettle();
      expect(store.currentProject.artifacts, isEmpty);
      expect(find.byType(UiDataTable), findsNothing);
      expect(find.text('update table view'), findsOneWidget);
      await finish(tester, events);
    },
  );
}
