import 'dart:convert';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:test/test.dart';

void main() {
  test('credentials encode as an RFC 7617 Basic header', () {
    const credentials = BasicCredentials(
      username: 'testuser',
      password: 'p@ss:word',
    );

    expect(
      credentials.authorizationHeader,
      'Basic ${base64.encode(utf8.encode('testuser:p@ss:word'))}',
    );
  });

  test('every request through the client carries the credential', () async {
    final seen = <String, String?>{};
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://ui.example:5080'),
      credentials: const BasicCredentials(
        username: 'testuser',
        password: 'secret',
      ),
      httpClient: MockClient((request) async {
        seen[request.url.path] = request.headers['authorization'];
        return http.Response('{}', 200);
      }),
    );

    // A plain POST and an SSE-shaped GET share one send() choke point.
    await client.openScene(shellName: 'desk', sceneKey: 'home', title: 'Home');
    await client.readChart('spend');

    expect(seen.keys, containsAll(['/owner/commands', '/kit/charts/spend']));
    final expected = 'Basic ${base64.encode(utf8.encode('testuser:secret'))}';
    for (final header in seen.values) {
      expect(header, expected);
    }
  });

  test('checkAuth reports rejection on 401 rather than throwing', () async {
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://ui.example:5080'),
      credentials: const BasicCredentials(username: 'a', password: 'b'),
      httpClient: MockClient((request) async {
        expect(request.url.path, '/auth/check');
        return http.Response('', 401);
      }),
    );

    expect(await client.checkAuth(), isFalse);
  });

  test('checkAuth accepts a gated 204 and an ungated 404', () async {
    Future<bool> check(int status) {
      final client = DigitalBrainUiClient(
        baseUri: Uri.parse('http://ui.example:5080'),
        httpClient: MockClient((request) async => http.Response('', status)),
      );
      return client.checkAuth();
    }

    expect(await check(204), isTrue);
    expect(await check(404), isTrue);
  });

  test('an unset credential leaves requests unauthenticated', () async {
    String? seen = 'unset';
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://ui.example:5080'),
      httpClient: MockClient((request) async {
        seen = request.headers['authorization'];
        return http.Response('', 202);
      }),
    );

    await client.openScene(shellName: 'desk', sceneKey: 'home', title: 'Home');

    expect(seen, isNull);
  });
}
