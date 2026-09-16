import 'dart:convert';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:test/test.dart';

void main() {
  test('voice request only transcribes and returns editable text', () async {
    final requests = <http.Request>[];
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('https://example.com'),
      httpClient: MockClient((request) async {
        requests.add(request);
        return http.Response('{"text":"review this draft"}', 200);
      }),
    );
    expect(
      await client.transcribeVoice(audioBytes: [1, 2, 3]),
      'review this draft',
    );
    expect(requests.single.url.path, '/agent/transcribe');
    expect(
      requests.single.headers['content-type'],
      contains('multipart/form-data'),
    );
    expect(requests.single.body, contains('name="audio"'));
  });
  test('document edits send expected revision and complete state', () async {
    late http.Request sent;
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('https://example.com'),
      httpClient: MockClient((request) async {
        sent = request;
        return http.Response('{"id":"artifact-one","revision":3}', 200);
      }),
    );
    await client.updateWorkspaceArtifact(
      'artifact-one',
      expectedRevision: 2,
      title: 'CRM',
      content: {
        'source': 'drawing',
        'editorState': {'zoom': 1.2},
      },
    );
    expect(sent.method, 'PUT');
    expect(jsonDecode(sent.body), {
      'expectedRevision': 2,
      'title': 'CRM',
      'content': {
        'source': 'drawing',
        'editorState': {'zoom': 1.2},
      },
    });
  });
}
