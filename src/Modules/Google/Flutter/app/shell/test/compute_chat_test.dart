import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/workspace/compute_usage_history.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_chat.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_store.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'workspace_remote_controller_test.dart' show MemoryPersistence;

void main() {
  test('saved preview and app receipt merge without losing settlement', () {
    final store = WorkspaceStore(persistence: MemoryPersistence());
    store.currentConversation.messages.addAll([
      {
        'id': 'operation',
        'role': 'ui',
        'card': {
          'kind': 'charge-receipt',
          'intentId': 'usage',
          'chargedCompute': 2,
        },
      },
      {
        'id': 'run/receipt',
        'role': 'receipt',
        'receipt': <String, dynamic>{
          'id': 'usage',
          'compute': .4,
          'shadow': true,
        },
      },
    ]);
    final item = savedComputeHistory(store.currentProject).single;
    expect(item.id, 'usage');
    expect(item.previewCompute, .4);
    expect(item.settledCompute, 2);
    expect(item.chargedCompute, isNull);
    (store.currentConversation.messages.last['receipt']
            as Map)['previewCompute'] =
        null;
    expect(
      savedComputeHistory(store.currentProject).single.previewCompute,
      isNull,
    );
    store.dispose();
  });

  testWidgets(
    'passive receipts leave chat while approvals and saved history remain',
    (tester) async {
      final store = WorkspaceStore(persistence: MemoryPersistence());
      store.currentConversation.messages.addAll([
        {
          'id': 'run/receipt',
          'role': 'receipt',
          'receipt': {'compute': .4, 'shadow': true},
        },
        {
          'id': 'charge',
          'role': 'ui',
          'card': {'kind': 'charge-receipt', 'chargedCompute': 2},
        },
        {
          'id': 'approval',
          'role': 'ui',
          'card': {'kind': 'consent-sheet', 'name': 'Calendar', 'estimate': 2},
        },
      ]);
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: WorkspaceChat(
              store: store,
              conversation: store.currentConversation,
              onArtifact: (_) {},
              onAttach: () {},
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.text('Receipt'), findsNothing);
      expect(find.text('Charged 2 Compute'), findsNothing);
      expect(find.text('Approve'), findsOneWidget);
      expect(savedComputeHistory(store.currentProject).map((e) => e.id), [
        'run',
        'charge',
      ]);
      expect(store.currentConversation.messages, hasLength(3));
      await tester.pumpWidget(const SizedBox());
      store.dispose();
    },
  );

  testWidgets(
    'late receipt invalidates Compute after run completion and stays out of chat',
    (tester) async {
      final store = WorkspaceStore(persistence: MemoryPersistence());
      final events = StreamController<AgentEvent>();
      var updates = 0;
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: WorkspaceChat(
              store: store,
              conversation: store.currentConversation,
              onArtifact: (_) {},
              onAttach: () {},
              onUsageChanged: () => updates++,
              onRun: ({
                required workspaceId,
                required threadId,
                required runId,
                parentRunId,
                modelProfile,
                required text,
              }) => events.stream,
            ),
          ),
        ),
      );
      await tester.enterText(find.byType(TextField), 'Hello');
      await tester.pump();
      await tester.tap(find.byTooltip('Send'));
      await tester.pump();
      events.add(AgentEvent({'type': 'RUN_FINISHED'}));
      events.add(
        AgentEvent({
          'type': 'RECEIPT',
          'id': 'canonical-id',
          'compute': .2,
          'shadow': true,
        }),
      );
      await tester.pump();
      expect(updates, 2);
      expect(
        savedComputeHistory(store.currentProject).single.id,
        'canonical-id',
      );
      expect(find.text('Receipt'), findsNothing);
      await events.close();
      await tester.pumpAndSettle(const Duration(milliseconds: 350));
      await tester.pumpWidget(const SizedBox());
      store.dispose();
    },
  );
}
