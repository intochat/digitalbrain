import 'dart:convert';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/workspace/app_launcher.dart';
import 'package:digitalbrain_flutter_shell/workspace/app_surface_host.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_store.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_voice.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

void main() {
  const root = 'scope/applications/assistant';

  testWidgets(
    'the assistant starts, renders its chat and voice, and relays a sent message',
    (tester) async {
      final requests = <String>[];
      final events = <Map<String, dynamic>>[];
      var messages = <Map<String, Object>>[];
      final client = DigitalBrainUiClient(
        baseUri: Uri.parse('http://test'),
        httpClient: MockClient((request) async {
          requests.add('${request.method} ${request.url.path}');
          final kind = request.url.queryParameters['kind'];
          final Object response = switch (request.url.path) {
            '/workspaces/one/applications/assistant/start' => {
              'surface': {'kind': 'surface', 'name': '$root/surface'},
            },
            '/workspaces/one/apps/event' => () {
              events.add(Map<String, dynamic>.from(jsonDecode(request.body)));
              messages = [
                {'id': 'u', 'role': 'User', 'text': 'hi'},
                {'id': 'a', 'role': 'Assistant', 'text': 'Hello!'},
              ];
              return {'delivered': true};
            }(),
            _ when kind == 'surface' => {
              'definition': {
                'children': [
                  {'kind': 'layout', 'name': '$root/layout'},
                ],
              },
            },
            _ when kind == 'layout' => {
              'definition': {
                'mode': 'column',
                'children': [
                  {'kind': 'uichat', 'name': '$root/chat'},
                  {'kind': 'voiceinput', 'name': '$root/voice'},
                ],
              },
            },
            _ when kind == 'uichat' => {
              'name': '$root/chat',
              'revision': messages.length,
              'messages': messages,
            },
            _ => {
              'name': '$root/voice',
              'revision': 1,
              'label': 'Hold to talk',
            },
          };
          return http.Response(jsonEncode(response), 200);
        }),
      );
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: AppSurfaceHost(
              client: client,
              workspace: 'one',
              artifact: WorkspaceArtifact(
                id: 'app-assistant',
                title: 'Assistant',
                kind: 'app',
                data: {'app': 'assistant'},
              ),
              onImage: (_) {},
              onChanged: () async {},
              onSaved: () {},
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();

      expect(
        requests.first,
        'POST /workspaces/one/applications/assistant/start',
      );
      expect(find.byType(WorkspaceVoiceButton), findsOneWidget);
      expect(find.byTooltip('Record a voice message'), findsOneWidget);

      await tester.enterText(find.byType(TextField), 'hi');
      await tester.testTextInput.receiveAction(TextInputAction.send);
      await tester.pumpAndSettle();

      expect(events, [
        {'kind': 'uichat', 'name': '$root/chat', 'value': 'hi'},
      ]);
      expect(find.text('Hello!'), findsOneWidget);
      await tester.pumpWidget(const SizedBox());
      await tester.pump(const Duration(seconds: 1));
    },
  );

  test('the assistant is offered in the launcher', () {
    expect(assistantLauncherEntry.launchKey, 'assistant');
    expect(assistantLauncherEntry.title, 'Assistant');
  });
}
