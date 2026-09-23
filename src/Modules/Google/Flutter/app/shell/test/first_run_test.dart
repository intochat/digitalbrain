import 'dart:async';
import 'dart:convert';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_app.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_store.dart';
import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:flutter/material.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_chat.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;

import 'workspace_remote_controller_test.dart' show MemoryPersistence;

class FirstRunClient extends http.BaseClient {
  final events = StreamController<List<int>>.broadcast();

  @override
  Future<http.StreamedResponse> send(http.BaseRequest request) async {
    if (request.url.path.endsWith('/events')) {
      return http.StreamedResponse(events.stream, 200);
    }
    final Object value;
    if (request.url.path.endsWith('/apps')) {
      value = const <Object>[];
    } else if (request.url.path.contains('/conversations/')) {
      value = {'revision': 1, 'activeRunId': null, 'turns': <Object>[]};
    } else {
      value = {
        'revision': 0,
        'windows': <Object>[],
        'firstRun': {
          'assistantId': 'intocaht',
          'assistantTitle': 'IntoChat',
          'prompts': [
            {
              'id': 'assistant',
              'label': 'Ask the assistant',
              'prompt': 'What can you do?',
              'source': 'assistant',
            },
            {
              'id': 'salesforce',
              'label': 'Work with Salesforce',
              'prompt': 'Show my Salesforce leads',
              'source': 'salesforce',
            },
          ],
        },
      };
    }
    return http.StreamedResponse(
      Stream.value(utf8.encode(jsonEncode(value))),
      200,
      headers: {'content-type': 'application/json'},
    );
  }
}

void main() {
  testWidgets('a new workspace shows the assistant starter prompts', (
    tester,
  ) async {
    final store = WorkspaceStore(persistence: MemoryPersistence());
    final api = FirstRunClient();
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://test'),
      httpClient: api,
    );
    await tester.pumpWidget(
      WorkspaceApp(
        store: store,
        programmingClient: client,
        connectedSources: const ['salesforce'],
      ),
    );
    await tester.pumpAndSettle();

    expect(find.byType(FirstRunView), findsOneWidget);
    expect(
      find.descendant(
        of: find.byType(FirstRunView),
        matching: find.text('IntoChat'),
      ),
      findsOneWidget,
    );
    expect(find.byType(WorkspaceChat), findsWidgets);
    expect(find.text('Work with Salesforce'), findsOneWidget);
    expect(find.text('Ask the assistant'), findsOneWidget);

    await tester.pumpWidget(const SizedBox());
    await api.events.close();
    store.dispose();
  });
}
