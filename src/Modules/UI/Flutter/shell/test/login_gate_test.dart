import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/auth/brain_session_gate.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

import 'support/shell_test_support.dart';

const _shellMarker = Key('signed-in-shell');

Widget _gate(ClientFactory createClient) => BrainSessionGate(
  createClient: createClient,
  builder: (client, status) => MaterialApp(
    home: Scaffold(body: Text(status ?? 'signed in', key: _shellMarker)),
  ),
);

ClientFactory _kernel({
  required int Function(BasicCredentials? credentials) status,
}) {
  return (credentials) => DigitalBrainUiClient(
    baseUri: Uri.parse('http://ui.example:5080'),
    credentials: credentials,
    httpClient: MockClient(
      (request) async => http.Response('', status(credentials)),
    ),
  );
}

void main() {
  testWidgets('a gated kernel prompts before the shell opens', (tester) async {
    await prepareShellSurface(tester);

    await tester.pumpWidget(
      _gate(_kernel(status: (credentials) => credentials == null ? 401 : 204)),
    );
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('login-submit')), findsOneWidget);
    expect(find.byKey(_shellMarker), findsNothing);
    // No stale error before the owner has tried anything.
    expect(find.byKey(const Key('login-error')), findsNothing);
  });

  testWidgets('correct credentials replace the prompt with the shell', (
    tester,
  ) async {
    await prepareShellSurface(tester);

    await tester.pumpWidget(
      _gate(
        _kernel(
          status: (credentials) => credentials?.password == 'right' ? 204 : 401,
        ),
      ),
    );
    await tester.pumpAndSettle();

    await tester.enterText(find.byKey(const Key('login-username')), 'testuser');
    await tester.enterText(find.byKey(const Key('login-password')), 'right');
    await tester.tap(find.byKey(const Key('login-submit')));
    await tester.pumpAndSettle();

    expect(find.byKey(_shellMarker), findsOneWidget);
  });

  testWidgets('wrong credentials keep the prompt and show the error', (
    tester,
  ) async {
    await prepareShellSurface(tester);

    await tester.pumpWidget(_gate(_kernel(status: (_) => 401)));
    await tester.pumpAndSettle();

    await tester.enterText(find.byKey(const Key('login-username')), 'testuser');
    await tester.enterText(find.byKey(const Key('login-password')), 'wrong');
    await tester.tap(find.byKey(const Key('login-submit')));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('login-error')), findsOneWidget);
    expect(find.byKey(_shellMarker), findsNothing);
  });

  testWidgets('an ungated kernel never prompts', (tester) async {
    await prepareShellSurface(tester);

    // 404: the gate is off, so /auth/check is not mapped at all.
    await tester.pumpWidget(_gate(_kernel(status: (_) => 404)));
    await tester.pumpAndSettle();

    expect(find.byKey(_shellMarker), findsOneWidget);
    expect(find.byKey(const Key('login-submit')), findsNothing);
  });

  testWidgets('an unreachable kernel opens the shell with the failure', (
    tester,
  ) async {
    await prepareShellSurface(tester);

    await tester.pumpWidget(
      _gate(
        (credentials) => DigitalBrainUiClient(
          baseUri: Uri.parse('http://ui.example:5080'),
          credentials: credentials,
          httpClient: MockClient(
            (request) async => throw const UnreachableKernel(),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    // Losing the kernel is not a credential problem: no prompt, same as before.
    expect(find.byKey(const Key('login-submit')), findsNothing);
    expect(find.byKey(_shellMarker), findsOneWidget);
  });
}

final class UnreachableKernel implements Exception {
  const UnreachableKernel();

  @override
  String toString() => 'kernel unreachable';
}
