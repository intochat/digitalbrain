import 'dart:convert';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/auth/brain_session_gate.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

void main() {
  testWidgets(
    'switch removes previous shell before logout and isolates the next account',
    (tester) async {
      var principal = 'alice';
      var removed = false;
      await tester.pumpWidget(
        BrainSessionGate(
          createClient: (_) => DigitalBrainUiClient(
            baseUri: Uri.parse('http://kernel'),
            httpClient: MockClient((request) async {
              switch (request.url.path) {
                case '/auth/check':
                  return http.Response('', 204);
                case '/identity/logout':
                  expect(removed, isTrue);
                  return http.Response('', 204);
                case '/identity/login':
                  principal = jsonDecode(request.body)['principalId'] as String;
                case '/identity/session':
                  break;
                default:
                  return http.Response('', 404);
              }
              return http.Response(
                jsonEncode({
                  'principalId': principal,
                  'accountId': '$principal-account',
                  'workspaceId': '$principal-home',
                }),
                200,
              );
            }),
          ),
          builder: (client, status, switchAccount) => MaterialApp(
            home: _SessionShell(
              key: ValueKey(client!.workspaceIdentity),
              onDispose: () => removed = true,
              child: Column(
                children: [
                  Text(client.workspaceIdentity),
                  TextButton(
                    onPressed: switchAccount,
                    child: const Text('Switch account'),
                  ),
                ],
              ),
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.text('alice|alice-account'), findsOneWidget);
      await tester.tap(find.text('Switch account'));
      await tester.pumpAndSettle();
      expect(find.text('alice|alice-account'), findsNothing);
      await tester.enterText(find.byKey(const Key('login-username')), 'bob');
      await tester.enterText(
        find.byKey(const Key('login-password')),
        'bob-password-123',
      );
      await tester.tap(find.byKey(const Key('login-submit')));
      await tester.pumpAndSettle();
      expect(find.text('bob|bob-account'), findsOneWidget);
      expect(find.text('alice|alice-account'), findsNothing);
    },
  );
}

class _SessionShell extends StatefulWidget {
  const _SessionShell({
    super.key,
    required this.child,
    required this.onDispose,
  });
  final Widget child;
  final VoidCallback onDispose;
  @override
  State<_SessionShell> createState() => _SessionShellState();
}

class _SessionShellState extends State<_SessionShell> {
  @override
  void dispose() {
    widget.onDispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => Scaffold(body: widget.child);
}
