import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/chat/agent_chat_app.dart';
import 'package:digitalbrain_flutter_shell/main.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_store.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;

class TestWorkspacePersistence implements WorkspacePersistence {
  @override
  Future<String?> read() async => null;
  @override
  Future<void> write(String value) async {}
}

class NoRequests extends http.BaseClient {
  int calls = 0;
  @override
  Future<http.StreamedResponse> send(http.BaseRequest request) async {
    calls++;
    throw StateError('Unexpected startup request: ${request.url}');
  }
}

void main() {
  testWidgets(
    'run errors and early disconnect preserve text and offer recovery',
    (tester) async {
      var events = StreamController<AgentEvent>();
      final parents = <String?>[];
      await tester.pumpWidget(
        AgentChatApp(
          onRun:
              ({
                required threadId,
                required runId,
                parentRunId,
                required text,
              }) {
                parents.add(parentRunId);
                return events.stream;
              },
        ),
      );
      await tester.enterText(find.byType(TextField), 'Question');
      await tester.tap(find.byTooltip('Send message'));
      events.add(
        AgentEvent({'type': 'RUN_STARTED', 'threadId': 't', 'runId': 'failed'}),
      );
      events.add(
        AgentEvent({
          'type': 'TEXT_MESSAGE_CONTENT',
          'messageId': 'm',
          'delta': 'Partial response',
        }),
      );
      events.add(
        AgentEvent({'type': 'RUN_ERROR', 'message': 'Session unavailable'}),
      );
      await tester.pump();
      expect(find.textContaining('Partial response'), findsWidgets);
      expect(find.text('Session unavailable'), findsOneWidget);
      expect(find.text('Start a new conversation'), findsOneWidget);
      expect(find.byTooltip('Send message'), findsOneWidget);
      unawaited(events.close());
      events = StreamController<AgentEvent>();
      await tester.enterText(find.byType(TextField), 'Try again');
      await tester.tap(find.byTooltip('Send message'));
      expect(parents.last, isNull);
      unawaited(events.close());
      await tester.pump();
      expect(
        find.text('The connection ended before the response completed.'),
        findsOneWidget,
      );
      await tester.tap(find.text('Start a new conversation'));
      await tester.pump();
      expect(find.text('How can I help?'), findsOneWidget);
    },
  );

  testWidgets(
    'unknown tool results remain readable and malformed sources are not links',
    (tester) async {
      final events = StreamController<AgentEvent>();
      final opened = <Uri>[];
      await tester.pumpWidget(
        AgentChatApp(
          onRun: ({
            required threadId,
            required runId,
            parentRunId,
            required text,
          }) => events.stream,
          onOpenUrl: (uri) async {
            opened.add(uri);
          },
        ),
      );
      await tester.enterText(find.byType(TextField), 'Find sources');
      await tester.tap(find.byTooltip('Send message'));
      events.add(
        AgentEvent({
          'type': 'TOOL_CALL_START',
          'toolCallId': 'a',
          'toolCallName': 'other',
        }),
      );
      events.add(
        AgentEvent({
          'type': 'TOOL_CALL_RESULT',
          'toolCallId': 'a',
          'content': {'count': 4},
        }),
      );
      events.add(
        AgentEvent({
          'type': 'TOOL_CALL_START',
          'toolCallId': 'b',
          'toolCallName': 'search_web',
        }),
      );
      events.add(
        AgentEvent({
          'type': 'TOOL_CALL_RESULT',
          'toolCallId': 'b',
          'content': {
            'results': [
              {'title': 'Invalid', 'url': 42},
              {'title': 'Unsafe', 'url': 'javascript:alert(1)'},
              {'title': 'Valid', 'url': 'https://example.com'},
            ],
          },
        }),
      );
      await tester.pump();
      expect(find.textContaining('"count": 4'), findsOneWidget);
      await tester.ensureVisible(find.text('Valid'));
      await tester.pump();
      await tester.tap(find.text('Valid'));
      expect(opened, [Uri.parse('https://example.com')]);
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox.shrink());
      unawaited(events.close());
      await tester.pump();
    },
  );

  testWidgets(
    'default shell starts project directory without graph or legacy streams',
    (tester) async {
      final transport = NoRequests();
      await tester.pumpWidget(
        buildShell(
          chat: 'main',
          workspaceStore: WorkspaceStore(
            persistence: TestWorkspacePersistence(),
          ),
          edge: DigitalBrainUiClient(
            baseUri: Uri.parse('http://localhost'),
            httpClient: transport,
          ),
        ),
      );
      await tester.pump();
      expect(find.text('Your projects'), findsWidgets);
      expect(find.byKey(const Key('workspace_graph')), findsNothing);
      expect(transport.calls, 0);
    },
  );

  testWidgets(
    'streams text and tools; carries session, prevents concurrent sends and resets',
    (tester) async {
      var stream = StreamController<AgentEvent>();
      final inputs = <Map<String, String?>>[];
      var canceled = false;
      await tester.pumpWidget(
        AgentChatApp(
          onRun:
              ({
                required threadId,
                required runId,
                parentRunId,
                required text,
              }) {
                inputs.add({
                  'threadId': threadId,
                  'runId': runId,
                  'parentRunId': parentRunId,
                  'text': text,
                });
                stream.onCancel = () {
                  canceled = true;
                };
                return stream.stream;
              },
        ),
      );
      await tester.enterText(find.byType(TextField), 'Hello');
      await tester.tap(find.byTooltip('Send message'));
      await tester.pump();
      expect(find.byTooltip('Stop response'), findsOneWidget);
      expect(tester.widget<TextField>(find.byType(TextField)).enabled, false);
      stream.add(
        AgentEvent({
          'type': 'RUN_STARTED',
          'threadId': 'saved',
          'runId': 'first',
        }),
      );
      stream.add(AgentEvent({'type': 'TEXT_MESSAGE_START', 'messageId': 'm'}));
      stream.add(
        AgentEvent({
          'type': 'TEXT_MESSAGE_CONTENT',
          'messageId': 'm',
          'delta': 'Hello there',
        }),
      );
      stream.add(
        AgentEvent({
          'type': 'TOOL_CALL_START',
          'toolCallId': 'c',
          'toolCallName': 'search_web',
        }),
      );
      stream.add(
        AgentEvent({
          'type': 'TOOL_CALL_RESULT',
          'toolCallId': 'c',
          'content': '{"answer":"Found it","results":[{"title":"Example source","url":"https://example.com","content":"Evidence"}]}',
        }),
      );
      await tester.pump();
      expect(find.textContaining('Hello there'), findsWidgets);
      expect(find.text('Example source'), findsOneWidget);
      stream.add(AgentEvent({'type': 'RUN_FINISHED'}));
      await tester.pump();
      expect(canceled, false);
      expect(find.byTooltip('Stop response'), findsOneWidget);
      expect(tester.widget<TextField>(find.byType(TextField)).enabled, false);
      unawaited(stream.close());
      await tester.pump();
      stream = StreamController<AgentEvent>();
      await tester.enterText(find.byType(TextField), 'Next');
      await tester.tap(find.byTooltip('Send message'));
      await tester.pump();
      expect(inputs.last['threadId'], 'saved');
      expect(inputs.last['parentRunId'], 'first');
      await tester.tap(find.byTooltip('Stop response'));
      await tester.pump();
      expect(canceled, true);
      unawaited(stream.close());
      await tester.pump();
      await tester.tap(find.byTooltip('New conversation'));
      await tester.pump();
      expect(find.text('How can I help?'), findsOneWidget);
      stream = StreamController<AgentEvent>();
      await tester.enterText(find.byType(TextField), 'Fresh');
      await tester.tap(find.byTooltip('Send message'));
      await tester.pump();
      expect(inputs.last['parentRunId'], isNull);
      expect(inputs.last['threadId'], isNot('saved'));
      await tester.pumpWidget(const SizedBox.shrink());
      unawaited(stream.close());
      await tester.pump();
    },
  );
}
