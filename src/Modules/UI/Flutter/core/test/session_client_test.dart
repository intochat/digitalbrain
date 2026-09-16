import 'dart:convert';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:test/test.dart';

void main() {
  test(
    'session auth covers workspace requests and isolates preference identity',
    () async {
      final client = DigitalBrainUiClient(
        baseUri: Uri.parse('https://brain.test/'),
        bearerToken: 'opaque-session',
        accountId: 'user-42',
        httpClient: MockClient((request) async {
          expect(request.headers['authorization'], 'Bearer opaque-session');
          return http.Response('[]', 200);
        }),
      );
      expect(client.workspaceIdentity, 'user-42');
      await client.listWorkspaceArtifacts();
    },
  );

  test('desktop links using Basic bootstrap then opaque session', () async {
    var calls = 0;
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('https://brain.test/'),
      credentials: BasicCredentials(
        username: 'owner',
        password: 'test-password',
      ),
      httpClient: MockClient((request) async {
        calls++;
        if (request.url.path == '/identity/owner/bootstrap') {
          expect(request.headers['x-digitalbrain-session-transport'], 'bearer');
          expect(request.headers['authorization'], startsWith('Basic '));
          return http.Response(jsonEncode({'token': 'session'}), 200);
        }
        expect(request.url.path, '/identity/link');
        expect(request.headers['authorization'], 'Bearer session');
        return http.Response('{"code":"one-use"}', 200);
      }),
    );
    expect(await client.createTelegramLinkCode(), 'one-use');
    expect(calls, 2);
  });
}
