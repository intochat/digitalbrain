import 'dart:convert';
import 'dart:typed_data';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

void main() {
  test(
    'voice sends audio to the selected workspace and returns draft text',
    () async {
      final api = DigitalBrainUiClient(
        baseUri: Uri.parse('http://test'),
        httpClient: MockClient((request) async {
          expect(request.method, 'POST');
          expect(request.url.path, '/brains/project/voice');
          expect(
            base64Decode((jsonDecode(request.body) as Map)['audio'] as String),
            [1, 2, 3],
          );
          return http.Response('{"text":"spoken draft"}', 200);
        }),
      );
      expect(
        await api.transcribeWorkspaceAudio(
          'project',
          Uint8List.fromList([1, 2, 3]),
        ),
        'spoken draft',
      );
    },
  );

  test(
    'voice exposes provider failure instead of returning a false success',
    () async {
      final api = DigitalBrainUiClient(
        baseUri: Uri.parse('http://test'),
        httpClient: MockClient(
          (_) async => http.Response('{"error":"Voice unavailable"}', 503),
        ),
      );
      await expectLater(
        api.transcribeWorkspaceAudio('project', Uint8List(4)),
        throwsA(isA<TableRequestException>()),
      );
    },
  );
}
