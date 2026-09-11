import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_chat.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_store.dart';
import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';

class _Memory implements WorkspacePersistence {
  @override
  Future<String?> read() async => null;
  @override
  Future<void> write(String data) async {}
}

void main() {
  testWidgets(
    'successful browser login resumes the original request once and preserves draft',
    (tester) async {
      final store = WorkspaceStore(persistence: _Memory());
      store.currentConversation.draft = 'My next question';
      store.currentConversation.messages.addAll([
        {
          'id': 'request',
          'role': 'user',
          'text': 'Draw my actual Salesforce objects',
        },
        {
          'id': 'login',
          'role': 'tool',
          'result': {
            'kind': 'connection',
            'service': 'salesforce',
            'status': 'authentication_required',
          },
        },
      ]);
      var connected = false;
      final prompts = <String>[];
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: WorkspaceChat(
              conversation: store.currentConversation,
              store: store,
              onArtifact: (_) {},
              onAttach: () {},
              onSalesforceConnected: () async => connected,
              onRun:
                  ({
                    required threadId,
                    required runId,
                    parentRunId,
                    required text,
                  }) {
                    prompts.add(text);
                    return Stream.value(AgentEvent({'type': 'RUN_FINISHED'}));
                  },
            ),
          ),
        ),
      );
      await tester.pump(const Duration(seconds: 3));
      expect(prompts, isEmpty);
      connected = true;
      await tester.pump(const Duration(seconds: 3));
      await tester.pumpAndSettle();
      expect(prompts.single, startsWith('Draw my actual Salesforce objects'));
      expect(store.currentConversation.draft, 'My next question');
      expect(find.text('Salesforce connected'), findsOneWidget);
      await tester.pump(const Duration(seconds: 6));
      expect(prompts, hasLength(1));
      await tester.pumpWidget(const SizedBox());
    },
  );

  testWidgets(
    'Salesforce sign-in card opens browser and continues the request',
    (tester) async {
      final store = WorkspaceStore(persistence: _Memory());
      store.currentConversation.messages.add({
        'id': 'login',
        'role': 'tool',
        'complete': true,
        'name': 'salesforce_current_account',
        'result': {
          'kind': 'connection',
          'service': 'salesforce',
          'status': 'authentication_required',
          'loginUrl': 'https://workspace.example/integrations/salesforce/login?request=fixture',
        },
      });
      Uri? opened;
      String? prompt;
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: WorkspaceChat(
              conversation: store.currentConversation,
              store: store,
              onArtifact: (_) {},
              onAttach: () {},
              onOpenUrl: (url) async {
                opened = url;
              },
              onRun:
                  ({
                    required threadId,
                    required runId,
                    parentRunId,
                    required text,
                  }) {
                    prompt = text;
                    return Stream.value(AgentEvent({'type': 'RUN_FINISHED'}));
                  },
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.text('Connect Salesforce'), findsOneWidget);
      await tester.tap(find.text('Sign in to Salesforce'));
      expect(opened?.path, '/integrations/salesforce/login');
      await tester.tap(find.text('Continue after sign-in'));
      await tester.pumpAndSettle();
      expect(prompt, contains('Salesforce setup review'));
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets(
    'Enter sends, Shift Enter keeps a draft, and stop retains partial text',
    (tester) async {
      final store = WorkspaceStore(persistence: _Memory());
      final events = StreamController<AgentEvent>();
      addTearDown(() {
        unawaited(events.close());
      });
      final prompts = <String>[];
      await tester.pumpWidget(
        MaterialApp(
          theme: UiTheme.workspace(Brightness.dark),
          home: Scaffold(
            body: WorkspaceChat(
              conversation: store.currentConversation,
              store: store,
              onArtifact: (_) {},
              onAttach: () {},
              onRun:
                  ({
                    required threadId,
                    required runId,
                    parentRunId,
                    required text,
                  }) {
                    prompts.add(text);
                    return events.stream;
                  },
            ),
          ),
        ),
      );
      await tester.enterText(
        find.byKey(const Key('chat-message-input')),
        'First line',
      );
      await tester.sendKeyDownEvent(LogicalKeyboardKey.shiftLeft);
      await tester.sendKeyEvent(LogicalKeyboardKey.enter);
      await tester.sendKeyUpEvent(LogicalKeyboardKey.shiftLeft);
      await tester.pump();
      expect(prompts, isEmpty);
      expect(store.currentConversation.draft, contains('\n'));
      await tester.sendKeyEvent(LogicalKeyboardKey.enter);
      await tester.pump();
      expect(prompts.single, startsWith('First line'));
      events.add(
        AgentEvent({
          'type': 'TEXT_MESSAGE_CONTENT',
          'messageId': 'm',
          'delta': 'Partial answer',
        }),
      );
      await tester.pump();
      await tester.tap(find.byTooltip('Stop response'));
      await tester.pumpAndSettle();
      expect(store.currentConversation.messages.last['text'], 'Partial answer');
      expect(find.byTooltip('Copy response'), findsOneWidget);
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets(
    'composer and specialist menu fit narrow windows with enlarged text',
    (tester) async {
      await tester.binding.setSurfaceSize(const Size(360, 780));
      addTearDown(() => tester.binding.setSurfaceSize(null));
      final store = WorkspaceStore(persistence: _Memory());
      store.setAgent('automation');
      await tester.pumpWidget(
        MaterialApp(
          theme: UiTheme.workspace(Brightness.light),
          home: MediaQuery(
            data: const MediaQueryData(textScaler: TextScaler.linear(2)),
            child: Scaffold(
              body: WorkspaceChat(
                conversation: store.currentConversation,
                store: store,
                onArtifact: (_) {},
                onAttach: () {},
              ),
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(tester.takeException(), isNull);
      await tester.tap(find.byTooltip('Choose specialist'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Salesforce Admin'));
      await tester.pumpAndSettle();
      expect(store.currentConversation.selectedAgentId, 'salesforce');
      expect(tester.takeException(), isNull);
    },
  );
}
