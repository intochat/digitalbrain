import 'dart:async';
import 'dart:convert';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_app.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_store.dart';
import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'workspace_remote_controller_test.dart' show MemoryPersistence;

void main() {
  testWidgets('chart tool result opens a chart rather than a table', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1500, 900);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final store = WorkspaceStore(persistence: MemoryPersistence());
    final events = StreamController<AgentEvent>.broadcast();
    await tester.pumpWidget(
      WorkspaceApp(
        store: store,
        onRun: ({
          required workspaceId,
          required threadId,
          required runId,
          parentRunId,
          required text,
        }) => events.stream,
      ),
    );
    await tester.pumpAndSettle();
    await tester.enterText(
      find.byKey(const Key('chat-message-input')),
      'pie chart companies by location',
    );
    await tester.pump();
    tester
        .widget<IconButton>(
          find.byWidgetPredicate((w) => w is IconButton && w.tooltip == 'Send'),
        )
        .onPressed!();
    await tester.pump();
    events.add(
      AgentEvent({
        'type': 'TOOL_CALL_RESULT',
        'toolCallId': 'chart-call',
        'content': jsonEncode({
          'kind': 'chart',
          'id': 'chart-locations',
          'title': 'Companies by location',
          'chartKind': 'pie',
          'points': [
            {'label': 'London', 'value': 8},
          ],
        }),
      }),
    );
    await tester.pumpAndSettle();
    expect(find.byType(UiChart), findsOneWidget);
    expect(find.byType(UiDataTable), findsNothing);
    await tester.pumpWidget(const SizedBox.shrink());
    await events.close();
    store.dispose();
  });
  testWidgets('assistant chart artifact opens as a pie chart window', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1500, 900);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final store = WorkspaceStore(persistence: MemoryPersistence());
    store.addArtifact(
      WorkspaceArtifact(
        id: 'chart-locations',
        title: 'Companies by location',
        kind: 'chart',
        data: {
          'chartKind': 'pie',
          'points': [
            {'label': 'London', 'value': 8},
            {'label': 'Bristol', 'value': 3},
          ],
        },
      ),
    );
    store.openArtifact('chart-locations');
    await tester.pumpWidget(WorkspaceApp(store: store));
    await tester.pumpAndSettle();
    expect(find.byType(UiChart), findsOneWidget);
    expect(find.textContaining('London'), findsWidgets);
    await tester.pumpWidget(const SizedBox.shrink());
    store.dispose();
  });
}
