import 'dart:convert';

import 'package:digitalbrain_flutter_shell/auth/telegram_session_gate.dart';
import 'package:digitalbrain_flutter_shell/main.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_store.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

import 'support/memory_workspace.dart';

void main() {
  testWidgets('linked Telegram owner opens the shared Projects homepage', (
    tester,
  ) async {
    final store = WorkspaceStore(
      seedProject: false,
      persistence: MemoryWorkspacePersistence(),
    );
    await tester.pumpWidget(
      TelegramSessionGate(
        initData: 'signed-proof',
        baseUri: Uri.parse('https://brain.test/'),
        httpClient: MockClient((request) async {
          expect(request.url.path, '/identity/login/telegram');
          expect(jsonDecode(request.body)['credential'], 'signed-proof');
          return http.Response(
            '{"token":"session","userId":"owner","isOwner":true}',
            200,
          );
        }),
        builder: (client, status) {
          expect(client!.workspaceIdentity, 'owner');
          return buildShell(chat: 'main', edge: null, workspaceStore: store);
        },
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('Your projects'), findsOneWidget);
    expect(find.text('Link and open projects'), findsNothing);
    expect(tester.takeException(), isNull);
  });

  testWidgets('unlinked account must redeem a code before workspace opens', (
    tester,
  ) async {
    var opened = false;
    await tester.pumpWidget(
      TelegramSessionGate(
        initData: 'proof',
        baseUri: Uri.parse('https://brain.test/'),
        httpClient: MockClient((request) async {
          if (request.url.path == '/identity/login/telegram') {
            return http.Response('', 401);
          }
          expect(request.url.path, '/identity/link/redeem/telegram');
          expect(jsonDecode(request.body)['code'], 'one-use-code');
          return http.Response(
            '{"token":"session","userId":"owner","isOwner":true}',
            200,
          );
        }),
        builder: (_, _) {
          opened = true;
          return const MaterialApp(home: Text('Projects'));
        },
      ),
    );
    await tester.pumpAndSettle();
    expect(opened, isFalse);
    await tester.enterText(find.byType(TextField), 'one-use-code');
    await tester.tap(find.text('Link and open projects'));
    await tester.pumpAndSettle();
    expect(opened, isTrue);
  });
}
